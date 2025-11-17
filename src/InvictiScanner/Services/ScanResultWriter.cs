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
