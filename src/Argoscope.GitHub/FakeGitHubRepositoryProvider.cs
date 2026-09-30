using Argoscope.Domain.Repositories;
using Argoscope.Domain.Snapshots;
using Argoscope.GitHub;

namespace Argoscope.GitHub;

/// <summary>
/// In-memory deterministic provider used by unit and integration tests. It
/// records every call and returns the configured observations. No HTTP or
/// external IO is performed.
/// </summary>
public sealed class FakeGitHubRepositoryProvider : IGitHubRepositoryProvider
{
    public string ProviderVersion { get; }

    public sealed record RepositoryFixture(
        string OwnerLogin,
        string Name,
        string NodeId,
        RepositoryVisibility Visibility,
        DateOnly? CreatedOnGithubAt,
        string? PrimaryLanguage,
        DateTimeOffset? LastActivityAtUtc);

    public sealed record MetricFixture(
        string OwnerLogin, string Name, string MetricName, DateOnly Date, double Value);

    public sealed record EngagementFixture(
        string OwnerLogin, string Name, DateOnly Date,
        int ExternalIssues, int ExternalPrs, int ExternalContribs,
        int OwnerIssues, int OwnerPrs, int OwnerContribs,
        int UnknownIssues, int UnknownPrs, int UnknownContribs);

    private readonly Dictionary<(string, string), RepositoryFixture> _repos = new();
    private readonly Dictionary<(string, string, string, DateOnly), MetricFixture> _metrics = new();
    private readonly Dictionary<(string, string, DateOnly), EngagementFixture> _engagement = new();
    private readonly List<(string Owner, string Name, string Metric, string? Cursor)> _metricCalls = new();
    private readonly List<(string Owner, string Name, string? Cursor)> _engagementCalls = new();
    private readonly List<(string Owner, string Name)> _repositoryCalls = new();

    public IReadOnlyList<(string Owner, string Name)> RepositoryCalls => _repositoryCalls;
    public IReadOnlyList<(string Owner, string Name, string Metric, string? Cursor)> MetricCalls => _metricCalls;
    public IReadOnlyList<(string Owner, string Name, string? Cursor)> EngagementCalls => _engagementCalls;

    /// <summary>Optional map of (owner,name) -> provider status to return.</summary>
    public Dictionary<(string, string), ProviderResultStatus> RepositoryStatuses { get; } = new();

    /// <summary>Optional per-metric override. If status is not Available/Partial, the page is empty and the status is returned.</summary>
    public Dictionary<(string, string, string), ProviderResultStatus> MetricStatuses { get; } = new();

    /// <summary>Optional cursor sequence per (owner, name, metric) that pages the underlying fixture.</summary>
    public Dictionary<(string, string, string), int> MetricPageSizes { get; } = new();

    public FakeGitHubRepositoryProvider(string providerVersion = "fake-1")
    {
        ProviderVersion = providerVersion;
    }

    public void AddRepository(RepositoryFixture fixture)
    {
        _repos[(fixture.OwnerLogin, fixture.Name)] = fixture;
    }

    public void AddMetric(MetricFixture fixture)
    {
        _metrics[(fixture.OwnerLogin, fixture.Name, fixture.MetricName, fixture.Date)] = fixture;
    }

    public void AddEngagement(EngagementFixture fixture)
    {
        _engagement[(fixture.OwnerLogin, fixture.Name, fixture.Date)] = fixture;
    }

    public Task<ProviderResult<RepositoryObservation>> GetRepositoryAsync(string ownerLogin, string name, CancellationToken cancellationToken)
    {
        _repositoryCalls.Add((ownerLogin, name));
        if (RepositoryStatuses.TryGetValue((ownerLogin, name), out var status))
        {
            return Task.FromResult(ProviderResult<RepositoryObservation>.Failure(status, $"fake-status-{status}"));
        }
        if (!_repos.TryGetValue((ownerLogin, name), out var fixture))
        {
            return Task.FromResult(ProviderResult<RepositoryObservation>.Failure(ProviderResultStatus.NotFound, "fake-not-found"));
        }
        var obs = new RepositoryObservation(
            fixture.NodeId,
            fixture.OwnerLogin,
            fixture.Name,
            fixture.Visibility,
            fixture.CreatedOnGithubAt,
            fixture.PrimaryLanguage,
            fixture.LastActivityAtUtc,
            fixture.LastActivityAtUtc ?? DateTimeOffset.UtcNow);
        return Task.FromResult(ProviderResult<RepositoryObservation>.Ok(obs));
    }

    public Task<ProviderPage<MetricObservation>> GetMetricPageAsync(string ownerLogin, string name, string metricName, string? cursor, DateOnly sinceDate, CancellationToken cancellationToken)
    {
        _metricCalls.Add((ownerLogin, name, metricName, cursor));
        if (MetricStatuses.TryGetValue((ownerLogin, name, metricName), out var status)
            && status is not (ProviderResultStatus.Available or ProviderResultStatus.Partial))
        {
            return Task.FromResult(new ProviderPage<MetricObservation>(Array.Empty<MetricObservation>(), null, status, null, $"fake-status-{status}"));
        }

        var pageSize = MetricPageSizes.GetValueOrDefault((ownerLogin, name, metricName), 30);
        var matching = _metrics
            .Where(kv => kv.Key.Item1 == ownerLogin && kv.Key.Item2 == name && kv.Key.Item3 == metricName && kv.Key.Item4 >= sinceDate)
            .OrderBy(kv => kv.Key.Item4)
            .ToList();

        var startIndex = ParseCursor(cursor);
        if (startIndex < 0 || startIndex > matching.Count)
        {
            startIndex = 0;
        }
        var slice = matching.Skip(startIndex).Take(pageSize).ToList();
        var endIndex = startIndex + slice.Count;
        var nextCursor = endIndex < matching.Count ? endIndex.ToString() : null;

        var items = slice.Select(kv =>
        {
            var f = kv.Value;
            return new MetricObservation(f.MetricName, f.Date, f.Value, f.Date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        }).ToList();

        return Task.FromResult(new ProviderPage<MetricObservation>(items, nextCursor, ProviderResultStatus.Available, null, null));
    }

    public Task<ProviderPage<EngagementObservation>> GetEngagementPageAsync(string ownerLogin, string name, string? cursor, DateTimeOffset sinceUtc, CancellationToken cancellationToken)
    {
        _engagementCalls.Add((ownerLogin, name, cursor));
        var sinceDate = DateOnly.FromDateTime(sinceUtc.UtcDateTime);
        var matching = _engagement
            .Where(kv => kv.Key.Item1 == ownerLogin && kv.Key.Item2 == name && kv.Key.Item3 >= sinceDate)
            .OrderBy(kv => kv.Key.Item3)
            .ToList();
        var startIndex = ParseCursor(cursor);
        if (startIndex < 0 || startIndex > matching.Count)
        {
            startIndex = 0;
        }
        const int pageSize = 30;
        var slice = matching.Skip(startIndex).Take(pageSize).ToList();
        var endIndex = startIndex + slice.Count;
        var nextCursor = endIndex < matching.Count ? endIndex.ToString() : null;

        var items = slice.Select(kv =>
        {
            var f = kv.Value;
            return new EngagementObservation(
                f.Date,
                f.ExternalIssues, f.ExternalPrs, f.ExternalContribs,
                f.OwnerIssues, f.OwnerPrs, f.OwnerContribs,
                f.UnknownIssues, f.UnknownPrs, f.UnknownContribs,
                f.Date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        }).ToList();

        return Task.FromResult(new ProviderPage<EngagementObservation>(items, nextCursor, ProviderResultStatus.Available, null, null));
    }

    private static int ParseCursor(string? cursor)
    {
        if (string.IsNullOrEmpty(cursor))
        {
            return 0;
        }
        return int.TryParse(cursor, out var n) ? n : -1;
    }
}
