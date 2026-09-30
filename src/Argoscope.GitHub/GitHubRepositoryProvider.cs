using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Argoscope.Domain.Repositories;
using Argoscope.Domain.Snapshots;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Argoscope.GitHub;

/// <summary>
/// Configuration for the real GitHub provider. The token is read from
/// <c>GitHub__Token</c> or a configured secret store; never written to disk
/// and never logged.
/// </summary>
public sealed class GitHubProviderOptions
{
    /// <summary>GitHub API base URL. Defaults to https://api.github.com.</summary>
    public string BaseUrl { get; set; } = "https://api.github.com";

    /// <summary>User agent reported to GitHub; required for REST calls.</summary>
    public string UserAgent { get; set; } = "argoscope/0.1";

    /// <summary>Accept header for the GraphQL API. Defaults to the public github v4 endpoint.</summary>
    public string GraphQlAccept { get; set; } = "application/vnd.github+json";

    /// <summary>Maximum pages to fetch per collection call; prevents unbounded pagination.</summary>
    public int MaxPagesPerRun { get; set; } = 100;

    /// <summary>Retry on rate limit after Retry-After, capped at this many seconds.</summary>
    public int MaxRetryAfterSeconds { get; set; } = 600;
}

/// <summary>
/// HTTP REST/GraphQL adapter for <see cref="IGitHubRepositoryProvider"/>. Only
/// used when an owner token is configured. Public-repository metadata can be
/// fetched anonymously; the GraphQL adapter authenticates with the token when
/// present. The real provider is intentionally conservative: it never retries
/// on 401/403/404, and redacts diagnostic codes that would otherwise leak
/// private repository identity to the UI.
/// </summary>
public sealed class GitHubRepositoryProvider : IGitHubRepositoryProvider
{
    public string ProviderVersion => "github-rest-1";

    private readonly HttpClient _http;
    private readonly GitHubProviderOptions _options;
    private readonly ILogger<GitHubRepositoryProvider> _logger;

    public GitHubRepositoryProvider(HttpClient http, IOptions<GitHubProviderOptions> options, ILogger<GitHubRepositoryProvider> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ProviderResult<RepositoryObservation>> GetRepositoryAsync(string ownerLogin, string name, CancellationToken cancellationToken)
    {
        var url = $"{_options.BaseUrl}/repos/{Uri.EscapeDataString(ownerLogin)}/{Uri.EscapeDataString(name)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.UserAgent.ParseAdd(_options.UserAgent);

        try
        {
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return ProviderResult<RepositoryObservation>.Failure(ProviderResultStatus.NotFound, "github-404");
            }
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                return ProviderResult<RepositoryObservation>.Failure(ProviderResultStatus.Unauthorized, "github-401");
            }
            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                if (IsRateLimited(response))
                {
                    return ProviderResult<RepositoryObservation>.Failure(ProviderResultStatus.RateLimited, "github-429", ReadRetryAfter(response));
                }
                return ProviderResult<RepositoryObservation>.Failure(ProviderResultStatus.Forbidden, "github-403");
            }
            if (!response.IsSuccessStatusCode)
            {
                return ProviderResult<RepositoryObservation>.Failure(ProviderResultStatus.Unavailable, $"github-{(int)response.StatusCode}");
            }

            var body = await response.Content.ReadFromJsonAsync<GitHubRepositoryDto>(cancellationToken: cancellationToken).ConfigureAwait(false);
            if (body is null || string.IsNullOrEmpty(body.NodeId))
            {
                return ProviderResult<RepositoryObservation>.Failure(ProviderResultStatus.Malformed, "github-empty");
            }

            var visibility = body.Private == true
                ? RepositoryVisibility.Private
                : (string.Equals(body.Visibility, "internal", StringComparison.OrdinalIgnoreCase) ? RepositoryVisibility.Internal : RepositoryVisibility.Public);

            DateOnly? createdOn = null;
            if (DateTimeOffset.TryParse(body.CreatedAt, out var created))
            {
                createdOn = DateOnly.FromDateTime(created.UtcDateTime);
            }

            DateTimeOffset? lastActivity = null;
            if (DateTimeOffset.TryParse(body.PushedAt, out var pushed))
            {
                lastActivity = pushed;
            }

            return ProviderResult<RepositoryObservation>.Ok(new RepositoryObservation(
                body.NodeId!, body.Owner.Login, body.Name, visibility, createdOn, body.Language, lastActivity,
                lastActivity ?? DateTimeOffset.UtcNow));
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "GitHub REST repository lookup failed for {Owner}/{Name}", ownerLogin, name);
            return ProviderResult<RepositoryObservation>.Failure(ProviderResultStatus.Unavailable, "github-network");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ProviderResult<RepositoryObservation>.Failure(ProviderResultStatus.Unavailable, "github-timeout");
        }
    }

    public Task<ProviderPage<MetricObservation>> GetMetricPageAsync(string ownerLogin, string name, string metricName, string? cursor, DateOnly sinceDate, CancellationToken cancellationToken)
    {
        // The real provider is not implemented in this MVP. The collection
        // service falls back to a single page that reuses the repository
        // observation plus zeroed counters, marked partial; the design record
        // and tests assert this contract.
        _ = ownerLogin; _ = name; _ = metricName; _ = cursor; _ = sinceDate;
        var page = new ProviderPage<MetricObservation>(
            Array.Empty<MetricObservation>(),
            null,
            ProviderResultStatus.Unavailable,
            null,
            "github-rest-not-implemented");
        return Task.FromResult(page);
    }

    public Task<ProviderPage<EngagementObservation>> GetEngagementPageAsync(string ownerLogin, string name, string? cursor, DateTimeOffset sinceUtc, CancellationToken cancellationToken)
    {
        _ = ownerLogin; _ = name; _ = cursor; _ = sinceUtc;
        var page = new ProviderPage<EngagementObservation>(
            Array.Empty<EngagementObservation>(),
            null,
            ProviderResultStatus.Unavailable,
            null,
            "github-rest-not-implemented");
        return Task.FromResult(page);
    }

    private static bool IsRateLimited(HttpResponseMessage response)
    {
        if ((int)response.StatusCode == 429) return true;
        if (response.Headers.TryGetValues("X-RateLimit-Remaining", out var remaining))
        {
            if (int.TryParse(remaining.FirstOrDefault(), out var n) && n <= 0) return true;
        }
        return false;
    }

    private DateTimeOffset? ReadRetryAfter(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Retry-After", out var values)) return null;
        var raw = values.FirstOrDefault();
        if (int.TryParse(raw, out var seconds))
        {
            var capped = Math.Min(seconds, _options.MaxRetryAfterSeconds);
            return DateTimeOffset.UtcNow.AddSeconds(capped);
        }
        if (DateTimeOffset.TryParse(raw, out var at))
        {
            return at;
        }
        return null;
    }

    private sealed class GitHubRepositoryDto
    {
        [JsonPropertyName("node_id")] public string? NodeId { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("private")] public bool? Private { get; set; }
        [JsonPropertyName("visibility")] public string? Visibility { get; set; }
        [JsonPropertyName("created_at")] public string? CreatedAt { get; set; }
        [JsonPropertyName("pushed_at")] public string? PushedAt { get; set; }
        [JsonPropertyName("language")] public string? Language { get; set; }
        [JsonPropertyName("owner")] public GitHubOwnerDto Owner { get; set; } = new();
    }

    private sealed class GitHubOwnerDto
    {
        [JsonPropertyName("login")] public string Login { get; set; } = string.Empty;
    }
}
