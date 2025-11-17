using InvictiScanner.Models;

namespace InvictiScanner.Configuration;

public sealed class AppSettings
{
    public InvictiSettings Invicti { get; init; } = new();
    public ScannerSettings Scanner { get; init; } = new();
    public DurationProfiles Duration { get; init; } = new();
}

public sealed class InvictiSettings
{
    public string BaseUrl { get; init; } = string.Empty;
    public string ApiToken { get; init; } = string.Empty;
    public string ApiId { get; init; } = string.Empty;
    public string Username { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
    public bool VerifyTls { get; init; } = true;
    public string DefaultAgentGroup { get; init; } = string.Empty;
    public string AgentName { get; init; } = string.Empty;
    public string WebsiteId { get; init; } = string.Empty;
    public string WebsiteName { get; init; } = string.Empty;
    public string WebsiteGroupName { get; init; } = string.Empty;
    public string TargetUri { get; init; } = string.Empty;
    public string ScanProfileId { get; init; } = string.Empty;
    public string IncrementalBaseScanId { get; init; } = string.Empty;
}

public sealed class ScannerSettings
{
    public string Action { get; init; } = ScannerAction.Scan.ToString().ToLowerInvariant();
    public string OutputPath { get; init; } = "/home/scanner/results";
    public int PollingIntervalSeconds { get; init; } = 15;
    public int MaximumWaitMinutes { get; init; } = 30;
}

public sealed class DurationProfiles
{
    public DurationProfile Short { get; init; } = DurationProfile.CreateShort();
    public DurationProfile Medium { get; init; } = DurationProfile.CreateMedium();
    public DurationProfile Long { get; init; } = DurationProfile.CreateLong();

    public DurationProfile Resolve(string? value)
    {
        var duration = DurationProfile.ParseDuration(value);
        return duration switch
        {
            ScanDuration.Long => Long ?? DurationProfile.CreateLong(),
            ScanDuration.Medium => Medium ?? DurationProfile.CreateMedium(),
            _ => Short ?? DurationProfile.CreateShort()
        };
    }
}

public sealed class DurationProfile
{
    public string Name { get; init; } = ScanDuration.Short.ToString().ToLowerInvariant();
    public string Description { get; init; } = string.Empty;
    public int MaxIssues { get; init; } = 50;
    public int MaxRuntimeMinutes { get; init; } = 15;
    public IssueSeverity MinimumSeverity { get; init; } = IssueSeverity.Medium;
    public bool UseIncremental { get; init; }
    public bool AllowFullScan { get; init; } = true;
    public string? ProfileName { get; init; }
    public IReadOnlyCollection<string> EndpointTags { get; init; } = Array.Empty<string>();

    public static DurationProfile CreateShort() => new()
    {
        Name = ScanDuration.Short.ToString().ToLowerInvariant(),
        Description = "Optimized for CI/CD feedback in minutes.",
        MaxIssues = 25,
        MaxRuntimeMinutes = 15,
        MinimumSeverity = IssueSeverity.High,
        UseIncremental = true,
        AllowFullScan = false,
        EndpointTags = new[] { "login", "health", "api-priority" }
    };

    public static DurationProfile CreateMedium() => new()
    {
        Name = ScanDuration.Medium.ToString().ToLowerInvariant(),
        Description = "Balanced coverage for nightly builds.",
        MaxIssues = 200,
        MaxRuntimeMinutes = 120,
        MinimumSeverity = IssueSeverity.Medium,
        UseIncremental = false,
        AllowFullScan = true,
        EndpointTags = new[] { "core", "authenticated" }
    };

    public static DurationProfile CreateLong() => new()
    {
        Name = ScanDuration.Long.ToString().ToLowerInvariant(),
        Description = "Full assessment for deep-dive testing.",
        MaxIssues = 1000,
        MaxRuntimeMinutes = 480,
        MinimumSeverity = IssueSeverity.BestPractice,
        UseIncremental = false,
        AllowFullScan = true
    };

    public static ScanDuration ParseDuration(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return ScanDuration.Short;
        }

        return Enum.TryParse<ScanDuration>(value, true, out var parsed) ? parsed : ScanDuration.Short;
    }
}

public enum ScanDuration
{
    Short,
    Medium,
    Long
}
