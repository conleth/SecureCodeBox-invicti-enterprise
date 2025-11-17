using System.Text.Json.Serialization;
using InvictiScanner.Configuration;

namespace InvictiScanner.Models;

public sealed class PagedResult<T>
{
    public IReadOnlyList<T> List { get; init; } = Array.Empty<T>();
    public int PageNumber { get; init; }
    public int PageSize { get; init; }
    public int TotalItemCount { get; init; }
}

public sealed class BasicIssueDto
{
    public Guid Id { get; init; }
    public string? Title { get; init; }
    public string? Url { get; init; }
    public IssueSeverity Severity { get; init; }
    public string? State { get; init; }
    public bool IsConfirmed { get; init; }
    public bool IsAddressed { get; init; }
    public string? FirstSeenDate { get; init; }
    public string? LastSeenDate { get; init; }
    [JsonPropertyName("CWE")]
    public string? Cwe { get; init; }
    public WebsiteInfo? Website { get; init; }
}

public sealed class WebsiteInfo
{
    public Guid Id { get; init; }
    public string? Name { get; init; }
    public string? RootUrl { get; init; }
}

public sealed class ScanTaskModel
{
    public Guid Id { get; init; }
    public string? TargetUri { get; init; }
    public string? Name { get; init; }
    public Guid? WebsiteId { get; init; }
    public string? WebsiteName { get; init; }
    public string? State { get; init; }
    public bool CrawlAndAttack { get; init; }
}

public sealed class ApiScanStatusModel
{
    public int CompletedSteps { get; init; }
    public int EstimatedSteps { get; init; }
    public int EstimatedLaunchTime { get; init; }
    public ScanState State { get; init; } = ScanState.Queued;
}

public enum ScanState
{
    Queued,
    Scanning,
    Archiving,
    Complete,
    Failed,
    Cancelled,
    Delayed,
    Pausing,
    Paused,
    Resuming,
    AsyncArchiving
}

public sealed class NewScanTaskApiModel
{
    public string TargetUri { get; init; } = string.Empty;
    public string CreateType { get; init; } = "Website";
    public Guid? WebsiteGroupId { get; init; }
    public string? AgentGroupName { get; init; }
    public string? AgentName { get; init; }
    public bool CrawlAndAttack { get; init; } = true;
    public bool IsMaxScanDurationEnabled { get; init; } = true;
    public int MaxScanDuration { get; init; } = 48;
    public bool ReportPolicyFailingUrls { get; init; } = true;
}

public sealed class NewScanTaskWithProfileApiModel
{
    public string ProfileName { get; init; } = string.Empty;
    public string TargetUri { get; init; } = string.Empty;
}

public sealed class IncrementalApiModel
{
    public bool IsMaxScanDurationEnabled { get; init; }
    public int MaxScanDuration { get; init; }
    public string? AgentGroupName { get; init; }
    public string? AgentName { get; init; }
    public Guid BaseScanId { get; init; }
}

public sealed class ScanMetadata
{
    public Guid? ScanId { get; set; }
    public string Mode { get; set; } = ScannerAction.Scan.ToString();
    public string Duration { get; set; } = ScanDuration.Short.ToString();
    public string? Target { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? Status { get; set; }
    public IReadOnlyCollection<string> Notes { get; set; } = Array.Empty<string>();
}
