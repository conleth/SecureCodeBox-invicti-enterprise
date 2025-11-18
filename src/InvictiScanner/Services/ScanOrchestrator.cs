using InvictiScanner.Configuration;
using InvictiScanner.Models;
using Microsoft.Extensions.Options;

namespace InvictiScanner.Services;

public sealed class ScanOrchestrator
{
    private readonly InvictiApiClient _client;
    private readonly DurationProfiles _durationProfiles;
    private readonly ScannerSettings _scannerSettings;
    private readonly InvictiSettings _invictiSettings;
    private readonly ScanResultWriter _writer;
    private readonly ILogger<ScanOrchestrator> _logger;

    public ScanOrchestrator(InvictiApiClient client, ScanResultWriter writer, IOptions<AppSettings> options, ILogger<ScanOrchestrator> logger)
    {
        _client = client;
        _writer = writer;
        _logger = logger;
        var settings = options.Value;
        _durationProfiles = settings.Duration;
        _scannerSettings = settings.Scanner;
        _invictiSettings = settings.Invicti;
    }

    public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        try
        {
            var commandLine = CommandLineArguments.Parse(args);
            var durationValue = commandLine["duration"] ?? Environment.GetEnvironmentVariable("SCB_SCAN_DURATION");
            var profile = _durationProfiles.Resolve(durationValue);
            var actionValue = ResolveAction(commandLine);
            var action = ScannerActionExtensions.Parse(actionValue);
            
            // Check if fire-and-forget mode is enabled
            var fireAndForget = ResolveFireAndForgetMode(commandLine);
            
            _logger.LogInformation("Scanner starting with action={Action}, duration={Duration}, fireAndForget={FireAndForget}", 
                action, profile.Name, fireAndForget);

            var metadata = new ScanMetadata
            {
                Mode = action.ToString().ToLowerInvariant(),
                Duration = profile.Name,
                Target = ResolveTargetDescription(),
                StartedAt = DateTimeOffset.UtcNow,
                Notes = new[] { profile.Description }
            };

            // Option B: Fetch existing issues only (called by SecureCodeBox webhook after scan completes)
            if (action == ScannerAction.Issues)
            {
                var issues = await _client.FetchIssuesAsync(profile, ResolveWebsiteName(), cancellationToken);
                metadata.CompletedAt = DateTimeOffset.UtcNow;
                metadata.Status = $"Fetched {issues.Count} issue(s)";
                await _writer.WriteAsync(issues, metadata, cancellationToken);
                _logger.LogInformation("Issues fetch completed: {Count} issues written", issues.Count);
                return 0;
            }

            // Option A: Launch scan
            var scan = await _client.LaunchScanAsync(profile, cancellationToken);
            if (scan is null)
            {
                _logger.LogError("Invicti did not return a scan identifier");
                metadata.CompletedAt = DateTimeOffset.UtcNow;
                metadata.Status = "LaunchFailed";
                await _writer.WriteAsync(Array.Empty<BasicIssueDto>(), metadata, cancellationToken);
                return 2;
            }

            metadata.ScanId = scan.Id;
            metadata.Target = scan.TargetUri ?? metadata.Target;

            // Fire-and-forget mode: return immediately with scan metadata only
            if (fireAndForget)
            {
                metadata.CompletedAt = DateTimeOffset.UtcNow;
                metadata.Status = "Launched";
                metadata.Notes = new[] 
                { 
                    profile.Description,
                    "Fire-and-forget mode enabled. Scan is running asynchronously.",
                    "SecureCodeBox will receive completion webhook from Invicti Enterprise.",
                    "Webhook will trigger Option B (Issues fetch) to collect results."
                };
                
                await _writer.WriteMetadataOnlyAsync(metadata, cancellationToken);
                _logger.LogInformation("Scan {ScanId} launched in fire-and-forget mode. Scan metadata written.", scan.Id);
                return 0;
            }

            // Traditional blocking mode: wait for scan completion
            var result = await WaitForCompletionAsync(scan.Id, profile, cancellationToken);
            metadata.CompletedAt = DateTimeOffset.UtcNow;
            metadata.Status = result?.State.ToString() ?? "Unknown";

            var issuesAfterScan = await _client.GetIssuesForScanAsync(profile, scan, cancellationToken);
            await _writer.WriteAsync(issuesAfterScan, metadata, cancellationToken);
            _logger.LogInformation("Scan {ScanId} completed with status {Status}. {Count} issues written.", 
                scan.Id, metadata.Status, issuesAfterScan.Count);

            return result is { State: ScanState.Complete } ? 0 : 3;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Operation cancelled");
            return 130;
        }
    }

    private string ResolveAction(CommandLineArguments arguments)
    {
        var issuesFlag = arguments["issues"] ?? Environment.GetEnvironmentVariable("SCB_FETCH_ISSUES_ONLY");
        if (!string.IsNullOrWhiteSpace(issuesFlag))
        {
            if (bool.TryParse(issuesFlag, out var fetchIssues) && fetchIssues)
            {
                return ScannerAction.Issues.ToString();
            }

            if (string.Equals(issuesFlag, "1", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(issuesFlag, "yes", StringComparison.OrdinalIgnoreCase))
            {
                return ScannerAction.Issues.ToString();
            }
        }

        var actionValue = arguments["action"] ?? Environment.GetEnvironmentVariable("SCB_ACTION") ?? _scannerSettings.Action;
        if (string.Equals(actionValue, "issues", StringComparison.OrdinalIgnoreCase))
        {
            return ScannerAction.Issues.ToString();
        }

        return actionValue;
    }

    /// <summary>
    /// Resolves whether fire-and-forget mode should be enabled.
    /// 
    /// Fire-and-forget mode allows the scanner to launch a scan in Invicti Enterprise
    /// and return immediately without waiting for completion. The scan continues
    /// asynchronously, and Invicti's webhook notifies SecureCodeBox when it completes.
    /// SecureCodeBox then calls back with Option B (Issues) to collect results.
    /// 
    /// Environment variables (in priority order):
    /// - SCB_SCANNER__FIREANDFORGET: "true"|"false"|"1"|"0"|"yes"|"no"
    /// - SCB_FIRE_AND_FORGET: Same options
    /// 
    /// Command line argument: --fireAndForget or --fire-and-forget
    /// </summary>
    private bool ResolveFireAndForgetMode(CommandLineArguments arguments)
    {
        // Check command line arguments first
        var fireAndForgetArg = arguments["fireAndForget"] ?? arguments["fire-and-forget"];
        if (!string.IsNullOrWhiteSpace(fireAndForgetArg))
        {
            if (bool.TryParse(fireAndForgetArg, out var result))
            {
                return result;
            }
            if (string.Equals(fireAndForgetArg, "1", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(fireAndForgetArg, "yes", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            return false;
        }

        // Check environment variables
        var envValue = Environment.GetEnvironmentVariable("SCB_SCANNER__FIREANDFORGET") ?? 
                      Environment.GetEnvironmentVariable("SCB_FIRE_AND_FORGET");
        
        if (!string.IsNullOrWhiteSpace(envValue))
        {
            if (bool.TryParse(envValue, out var result))
            {
                return result;
            }
            if (string.Equals(envValue, "1", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(envValue, "yes", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            return false;
        }

        // Default to false (traditional blocking mode)
        return false;
    }


    private async Task<ApiScanStatusModel?> WaitForCompletionAsync(Guid scanId, DurationProfile profile, CancellationToken cancellationToken)
    {
        var pollInterval = TimeSpan.FromSeconds(Math.Max(5, _scannerSettings.PollingIntervalSeconds));
        var timeout = TimeSpan.FromMinutes(Math.Min(_scannerSettings.MaximumWaitMinutes, profile.MaxRuntimeMinutes));
        _logger.LogInformation("Waiting up to {Timeout} for scan {ScanId} completion", timeout, scanId);
        var deadline = DateTimeOffset.UtcNow + timeout;
        ApiScanStatusModel? latest = null;

        while (DateTimeOffset.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
        {
            latest = await _client.GetScanStatusAsync(scanId, cancellationToken);
            if (latest is null)
            {
                _logger.LogWarning("Scan {ScanId} status endpoint returned no data", scanId);
                return null;
            }

            _logger.LogInformation("Scan {ScanId} state={State} ({Completed}/{Estimated} steps)", scanId, latest.State, latest.CompletedSteps, latest.EstimatedSteps);
            if (latest.State is ScanState.Complete or ScanState.Cancelled or ScanState.Failed)
            {
                return latest;
            }

            await Task.Delay(pollInterval, cancellationToken);
        }

        _logger.LogWarning("Scan {ScanId} did not finish within {Timeout}", scanId, timeout);
        return latest;
    }

    private string ResolveTargetDescription()
    {
        if (!string.IsNullOrWhiteSpace(_invictiSettings.WebsiteName))
        {
            return _invictiSettings.WebsiteName;
        }

        return _invictiSettings.TargetUri;
    }

    private string? ResolveWebsiteName()
    {
        if (!string.IsNullOrWhiteSpace(_invictiSettings.WebsiteName))
        {
            return _invictiSettings.WebsiteName;
        }

        if (Uri.TryCreate(_invictiSettings.TargetUri, UriKind.Absolute, out var uri))
        {
            return uri.Host;
        }

        return null;
    }
}
