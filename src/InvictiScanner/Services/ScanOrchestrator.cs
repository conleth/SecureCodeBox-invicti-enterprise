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
            _logger.LogInformation("Scanner starting with action={Action}, duration={Duration}", action, profile.Name);

            var metadata = new ScanMetadata
            {
                Mode = action.ToString().ToLowerInvariant(),
                Duration = profile.Name,
                Target = ResolveTargetDescription(),
                StartedAt = DateTimeOffset.UtcNow,
                Notes = new[] { profile.Description }
            };

            if (action == ScannerAction.Issues)
            {
                var issues = await _client.FetchIssuesAsync(profile, ResolveWebsiteName(), cancellationToken);
                metadata.CompletedAt = DateTimeOffset.UtcNow;
                metadata.Status = $"Fetched {issues.Count} issue(s)";
                await _writer.WriteAsync(issues, metadata, cancellationToken);
                return 0;
            }

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

            var result = await WaitForCompletionAsync(scan.Id, profile, cancellationToken);
            metadata.CompletedAt = DateTimeOffset.UtcNow;
            metadata.Status = result?.State.ToString() ?? "Unknown";

            var issuesAfterScan = await _client.GetIssuesForScanAsync(profile, scan, cancellationToken);
            await _writer.WriteAsync(issuesAfterScan, metadata, cancellationToken);

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
