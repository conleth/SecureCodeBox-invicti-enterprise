using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using InvictiScanner.Configuration;
using InvictiScanner.Models;
using Microsoft.Extensions.Options;

namespace InvictiScanner.Services;

public sealed class InvictiApiClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<InvictiApiClient> _logger;
    private readonly InvictiSettings _settings;

    public InvictiApiClient(HttpClient httpClient, IOptions<AppSettings> options, ILogger<InvictiApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _settings = options.Value.Invicti;
    }

    public async Task<ScanTaskModel?> LaunchScanAsync(DurationProfile profile, CancellationToken cancellationToken)
    {
        if (profile.UseIncremental && Guid.TryParse(_settings.IncrementalBaseScanId, out var baseScanId))
        {
            _logger.LogInformation("Starting incremental scan {ScanId} using duration profile {Profile}", baseScanId, profile.Name);
            var incremental = new IncrementalApiModel
            {
                BaseScanId = baseScanId,
                AgentGroupName = NullIfEmpty(_settings.DefaultAgentGroup),
                AgentName = NullIfEmpty(_settings.AgentName),
                IsMaxScanDurationEnabled = true,
                MaxScanDuration = Math.Max(1, (int)Math.Ceiling(profile.MaxRuntimeMinutes / 60d))
            };

            var incrementalResponse = await _httpClient.PostAsJsonAsync("/api/1.0/scans/incremental", incremental, SerializerOptions, cancellationToken);
            incrementalResponse.EnsureSuccessStatusCode();
            return await incrementalResponse.Content.ReadFromJsonAsync<ScanTaskModel>(SerializerOptions, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(profile.ProfileName) || !string.IsNullOrWhiteSpace(_settings.ScanProfileId))
        {
            var profileName = !string.IsNullOrWhiteSpace(profile.ProfileName) ? profile.ProfileName : _settings.ScanProfileId;
            _logger.LogInformation("Starting scan using Invicti profile {ProfileName}", profileName);
            var profilePayload = new NewScanTaskWithProfileApiModel
            {
                ProfileName = profileName!,
                TargetUri = ResolveTargetUri()
            };

            var profileResponse = await _httpClient.PostAsJsonAsync("/api/1.0/scans/newwithprofile", profilePayload, SerializerOptions, cancellationToken);
            profileResponse.EnsureSuccessStatusCode();
            return await profileResponse.Content.ReadFromJsonAsync<ScanTaskModel>(SerializerOptions, cancellationToken);
        }

        _logger.LogInformation("Starting ad hoc scan for {Uri} with profile {Profile}", _settings.TargetUri, profile.Name);
        var payload = new NewScanTaskApiModel
        {
            TargetUri = ResolveTargetUri(),
            CreateType = "Website",
            AgentGroupName = NullIfEmpty(_settings.DefaultAgentGroup),
            AgentName = NullIfEmpty(_settings.AgentName),
            CrawlAndAttack = profile.AllowFullScan,
            IsMaxScanDurationEnabled = true,
            MaxScanDuration = Math.Max(1, (int)Math.Ceiling(profile.MaxRuntimeMinutes / 60d)),
            ReportPolicyFailingUrls = profile.AllowFullScan
        };

        var response = await _httpClient.PostAsJsonAsync("/api/1.0/scans/new", payload, SerializerOptions, cancellationToken);
        response.EnsureSuccessStatusCode();
        var scans = await response.Content.ReadFromJsonAsync<List<ScanTaskModel>>(SerializerOptions, cancellationToken);
        return scans?.FirstOrDefault();
    }

    public async Task<ApiScanStatusModel?> GetScanStatusAsync(Guid scanId, CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetAsync($"/api/1.0/scans/status/{scanId}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ApiScanStatusModel>(SerializerOptions, cancellationToken);
    }

    public Task<IReadOnlyList<BasicIssueDto>> GetIssuesForScanAsync(DurationProfile profile, ScanTaskModel? scan, CancellationToken cancellationToken)
    {
        var websiteName = !string.IsNullOrWhiteSpace(scan?.WebsiteName)
            ? scan!.WebsiteName
            : NullIfEmpty(_settings.WebsiteName);

        return FetchIssuesAsync(profile.MinimumSeverity, profile.MaxIssues, websiteName, NullIfEmpty(_settings.WebsiteGroupName), cancellationToken);
    }

    public Task<IReadOnlyList<BasicIssueDto>> FetchIssuesAsync(DurationProfile profile, string? websiteName, CancellationToken cancellationToken)
        => FetchIssuesAsync(profile.MinimumSeverity, profile.MaxIssues, websiteName, NullIfEmpty(_settings.WebsiteGroupName), cancellationToken);

    public async Task<IReadOnlyList<BasicIssueDto>> FetchIssuesAsync(IssueSeverity minimumSeverity, int maxIssues, string? websiteName, string? websiteGroupName, CancellationToken cancellationToken)
    {
        var severityFilter = minimumSeverity;
        var targetIssues = Math.Max(1, maxIssues);
        var results = new List<BasicIssueDto>();
        var page = 1;

        while (results.Count < targetIssues)
        {
            var remaining = Math.Min(200, targetIssues - results.Count);
            var query = new Dictionary<string, string?>
            {
                ["severity"] = severityFilter.ToString(),
                ["page"] = page.ToString(CultureInfo.InvariantCulture),
                ["pageSize"] = remaining.ToString(CultureInfo.InvariantCulture),
                ["sortType"] = "Descending",
                ["sourceType"] = "InvictiDast"
            };

            if (!string.IsNullOrWhiteSpace(websiteName))
            {
                query["webSiteName"] = websiteName;
            }

            if (!string.IsNullOrWhiteSpace(websiteGroupName))
            {
                query["websiteGroupName"] = websiteGroupName;
            }

            var path = HttpQueryBuilder.WithQuery("/api/1.0/issues", query);
            var response = await _httpClient.GetAsync(path, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                break;
            }

            response.EnsureSuccessStatusCode();
            var payload = await response.Content.ReadFromJsonAsync<PagedResult<BasicIssueDto>>(SerializerOptions, cancellationToken);
            if (payload?.List is null || payload.List.Count == 0)
            {
                break;
            }

            foreach (var issue in payload.List)
            {
                if (!issue.Severity.IsAtLeast(minimumSeverity))
                {
                    continue;
                }

                results.Add(issue);
                if (results.Count >= targetIssues)
                {
                    break;
                }
            }

            if (payload.List.Count < remaining)
            {
                break;
            }

            page++;
        }

        _logger.LogInformation("Pulled {Count} issue(s) from Invicti", results.Count);
        return results;
    }

    private string ResolveTargetUri()
    {
        if (!string.IsNullOrWhiteSpace(_settings.TargetUri))
        {
            return _settings.TargetUri;
        }

        throw new InvalidOperationException("Invicti:TargetUri must be provided either via configuration or SCB_INVICTI__TARGETURI environment variable.");
    }

    internal static void ConfigureHttpClient(HttpClient client, InvictiSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            throw new InvalidOperationException("Invicti:BaseUrl is missing.");
        }

        client.BaseAddress = EnsureBaseUri(settings.BaseUrl);
        client.Timeout = TimeSpan.FromMinutes(10);
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.UserAgent.ParseAdd("securecodebox-invicti-scanner/1.0");

        if (!string.IsNullOrWhiteSpace(settings.ApiToken) || !string.IsNullOrWhiteSpace(settings.ApiId))
        {
            var tokenValue = !string.IsNullOrWhiteSpace(settings.ApiId)
                ? $"{settings.ApiId}:{settings.ApiToken}"
                : settings.ApiToken;
            client.DefaultRequestHeaders.Add("X-Auth", tokenValue);
        }

        if (!string.IsNullOrWhiteSpace(settings.Username) && !string.IsNullOrWhiteSpace(settings.Password))
        {
            var basic = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.Username}:{settings.Password}"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basic);
        }
    }

    internal static HttpMessageHandler BuildHandler(InvictiSettings settings)
    {
        if (settings.VerifyTls)
        {
            return new HttpClientHandler();
        }

        return new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };
    }

    private static Uri EnsureBaseUri(string value)
    {
        var normalized = value.EndsWith('/') ? value : value + "/";
        return new Uri(normalized, UriKind.Absolute);
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
