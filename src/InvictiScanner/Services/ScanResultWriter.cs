using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using InvictiScanner.Configuration;
using InvictiScanner.Models;
using Microsoft.Extensions.Options;

namespace InvictiScanner.Services;

public sealed class ScanResultWriter
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly ScannerSettings _settings;
    private readonly ILogger<ScanResultWriter> _logger;

    public ScanResultWriter(IOptions<AppSettings> options, ILogger<ScanResultWriter> logger)
    {
        _settings = options.Value.Scanner;
        _logger = logger;
    }

    public async Task WriteAsync(IReadOnlyCollection<BasicIssueDto> issues, ScanMetadata metadata, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_settings.OutputPath);
        var findingsPath = Path.Combine(_settings.OutputPath, "findings.json");
        var metadataPath = Path.Combine(_settings.OutputPath, "scan-metadata.json");
        var findings = issues.Select(ToFinding).ToArray();

        await using (var findingsStream = File.Create(findingsPath))
        {
            await JsonSerializer.SerializeAsync(findingsStream, findings, SerializerOptions, cancellationToken);
        }

        await using (var metadataStream = File.Create(metadataPath))
        {
            await JsonSerializer.SerializeAsync(metadataStream, metadata, SerializerOptions, cancellationToken);
        }

        _logger.LogInformation("Wrote {Count} finding(s) to {Path}", findings.Length, findingsPath);
    }

    /// <summary>
    /// Writes scan metadata only (without findings) for fire-and-forget mode.
    /// 
    /// This method is used when the scanner launches a scan and returns immediately
    /// without waiting for completion. Only metadata is written, which includes:
    /// - Scan ID for webhook callback
    /// - Status ("Launched")
    /// - Timestamps and target information
    /// - Notes explaining the fire-and-forget workflow
    /// 
    /// The actual findings will be written later when SecureCodeBox webhook
    /// triggers Option B (Issues fetch) after Invicti completes the scan.
    /// </summary>
    public async Task WriteMetadataOnlyAsync(ScanMetadata metadata, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_settings.OutputPath);
        var metadataPath = Path.Combine(_settings.OutputPath, "scan-metadata.json");
        
        // Create empty findings array for consistency with schema
        var emptyFindings = Array.Empty<SecureCodeBoxFinding>();
        var findingsPath = Path.Combine(_settings.OutputPath, "findings.json");

        // Write empty findings
        await using (var findingsStream = File.Create(findingsPath))
        {
            await JsonSerializer.SerializeAsync(findingsStream, emptyFindings, SerializerOptions, cancellationToken);
        }

        // Write metadata with detailed notes about the fire-and-forget mode
        await using (var metadataStream = File.Create(metadataPath))
        {
            await JsonSerializer.SerializeAsync(metadataStream, metadata, SerializerOptions, cancellationToken);
        }

        _logger.LogInformation(
            "Wrote metadata-only result for scan {ScanId} in fire-and-forget mode to {Path}. " +
            "Awaiting Invicti webhook callback to fetch final results.", 
            metadata.ScanId, metadataPath);
    }


    private static SecureCodeBoxFinding ToFinding(BasicIssueDto issue)
    {
        var attributes = new Dictionary<string, object?>
        {
            ["id"] = issue.Id,
            ["state"] = issue.State,
            ["isConfirmed"] = issue.IsConfirmed,
            ["isAddressed"] = issue.IsAddressed,
            ["firstSeenDate"] = issue.FirstSeenDate,
            ["lastSeenDate"] = issue.LastSeenDate,
            ["cwe"] = issue.Cwe,
            ["website"] = issue.Website?.Name ?? issue.Website?.RootUrl
        };

        return new SecureCodeBoxFinding
        {
            Name = issue.Title ?? issue.Id.ToString(),
            Category = "Invicti Issue",
            Severity = issue.Severity.ToString().ToLowerInvariant(),
            Description = $"{issue.Title ?? "Unknown"} (Severity: {issue.Severity})",
            Location = issue.Url,
            AttributeIdentifier = issue.Cwe,
            Attributes = attributes
        };
    }
}
