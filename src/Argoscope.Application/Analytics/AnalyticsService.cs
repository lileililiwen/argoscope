using Argoscope.Application.Benchmarks;
using Argoscope.Application.Collection;
using Argoscope.Application.Common;
using Argoscope.Application.Engagement;
using Argoscope.Application.Metrics;
using Argoscope.Domain.Common;
using Argoscope.Domain.Engagement;
using Argoscope.Domain.Memberships;
using Argoscope.Domain.Portfolios;
using Argoscope.Domain.Repositories;
using Argoscope.Domain.Snapshots;

namespace Argoscope.Application.Analytics;

/// <summary>One repository's full overview row in the portfolio dashboard.</summary>
public sealed record OverviewRow(
    Guid MembershipId,
    Guid RepositoryId,
    string NodeId,
    string OwnerLogin,
    string Name,
    string Role,
    string? Category,
    string Lifecycle,
    IReadOnlyDictionary<string, VelocityResult> Velocities,
    AccelerationResult? Acceleration,
    EngagementSummary? Engagement,
    DateTimeOffset? LastSnapshotAtUtc,
    ProviderResultStatus LatestStatus,
    string? DiagnosticCode);

public sealed record PortfolioOverview(
    Guid PortfolioId,
    string Window,
    DateOnly WindowStart,
    DateOnly WindowEnd,
    DateTimeOffset AsOfUtc,
    IReadOnlyList<OverviewRow> Rows);

/// <summary>
/// Read-side service that composes metric/engagement/benchmark/ranking data
/// for a single portfolio view. Pure with respect to the supplied stores.
/// </summary>
public sealed class AnalyticsService
{
    private readonly IPortfolioRepository _portfolios;
    private readonly IRepositoryStore _repositories;
    private readonly IMembershipStore _memberships;
    private readonly IMetricSnapshotStore _snapshots;
    private readonly IEngagementStore _engagement;
    private readonly IClock _clock;

    public AnalyticsService(
        IPortfolioRepository portfolios,
        IRepositoryStore repositories,
        IMembershipStore memberships,
        IMetricSnapshotStore snapshots,
        IEngagementStore engagement,
        IClock clock)
    {
        _portfolios = portfolios;
        _repositories = repositories;
        _memberships = memberships;
        _snapshots = snapshots;
        _engagement = engagement;
        _clock = clock;
    }

    public async Task<PortfolioOverview?> BuildOverviewAsync(
        Id<Portfolio> portfolioId,
        string window,
        DateTimeOffset asOfUtc,
        CancellationToken cancellationToken)
    {
        var portfolio = await _portfolios.FindAsync(portfolioId, cancellationToken).ConfigureAwait(false);
        if (portfolio is null)
        {
            return null;
        }

        var (windowDays, windowStart, windowEnd) = ParseWindow(window, asOfUtc);
        var memberships = await _memberships.ListByPortfolioAsync(portfolioId, cancellationToken).ConfigureAwait(false);
        var rows = new List<OverviewRow>(memberships.Count);
        foreach (var m in memberships)
        {
            var repo = await _repositories.FindAsync(m.RepositoryId, cancellationToken).ConfigureAwait(false);
            if (repo is null)
            {
                continue;
            }
            var repoSnapshots = await _snapshots.ListByRepositoryAsync(repo.Id, cancellationToken).ConfigureAwait(false);
            var series = new MetricSeries(repoSnapshots);
            var velocities = new Dictionary<string, VelocityResult>(StringComparer.Ordinal)
            {
                ["7d"] = MetricCalculator.ComputeVelocity(series, Domain.Snapshots.MetricNames.Stars, 7, asOfUtc),
                ["30d"] = MetricCalculator.ComputeVelocity(series, Domain.Snapshots.MetricNames.Stars, 30, asOfUtc),
                ["30d_forks"] = MetricCalculator.ComputeVelocity(series, Domain.Snapshots.MetricNames.Forks, 30, asOfUtc),
            };
            var acceleration = MetricCalculator.ComputeAcceleration(series, Domain.Snapshots.MetricNames.Stars, asOfUtc);
            var engagementBuckets = await _engagement.ListByRepositoryAsync(repo.Id, cancellationToken).ConfigureAwait(false);
            EngagementSummary? engagement = null;
            if (engagementBuckets.Count > 0)
            {
                engagement = EngagementAggregator.Summarize(new EngagementSeries(engagementBuckets), windowStart, windowEnd, asOfUtc);
            }
            var (latest, status, diag) = LatestSnapshotStatus(repoSnapshots);
            rows.Add(new OverviewRow(
                m.Id.Value,
                repo.Id.Value,
                repo.NodeId,
                repo.OwnerLogin,
                repo.Name,
                m.Role.ToString(),
                m.Category,
                m.Lifecycle,
                velocities,
                acceleration,
                engagement,
                latest?.CollectedAtUtc,
                status,
                diag));
        }
        return new PortfolioOverview(portfolio.Id.Value, $"{windowDays}d", windowStart, windowEnd, asOfUtc, rows);
    }

    public async Task<RepositoryMetrics?> BuildRepositoryMetricsAsync(Id<Repository> repositoryId, DateTimeOffset asOfUtc, CancellationToken cancellationToken)
    {
        var repo = await _repositories.FindAsync(repositoryId, cancellationToken).ConfigureAwait(false);
        if (repo is null)
        {
            return null;
        }
        var repoSnapshots = await _snapshots.ListByRepositoryAsync(repo.Id, cancellationToken).ConfigureAwait(false);
        var series = new MetricSeries(repoSnapshots);
        var velocities = new Dictionary<string, VelocityResult>(StringComparer.Ordinal);
        foreach (var name in Domain.Snapshots.MetricNames.All)
        {
            velocities[$"30d_{name}"] = MetricCalculator.ComputeVelocity(series, name, 30, asOfUtc);
        }
        var acceleration = MetricCalculator.ComputeAcceleration(series, Domain.Snapshots.MetricNames.Stars, asOfUtc);
        var engagementBuckets = await _engagement.ListByRepositoryAsync(repo.Id, cancellationToken).ConfigureAwait(false);
        var engagement = engagementBuckets.Count == 0
            ? null
            : EngagementAggregator.Summarize(new EngagementSeries(engagementBuckets),
                asOfUtc.UtcDateTime.AddDays(-30).ToDateOnly(), asOfUtc.UtcDateTime.ToDateOnly(), asOfUtc);
        var (latest, status, diag) = LatestSnapshotStatus(repoSnapshots);
        return new RepositoryMetrics(
            repo.Id.Value, repo.NodeId, repo.OwnerLogin, repo.Name, repo.Visibility, repo.CreatedOnGithubAt, repo.PrimaryLanguage,
            latest?.CollectedAtUtc, status, diag, velocities, acceleration, engagement);
    }

    public async Task<BenchmarkReport?> BuildBenchmarksAsync(Id<Portfolio> portfolioId, string window, DateTimeOffset asOfUtc, CancellationToken cancellationToken)
    {
        var portfolio = await _portfolios.FindAsync(portfolioId, cancellationToken).ConfigureAwait(false);
        if (portfolio is null)
        {
            return null;
        }
        var memberships = await _memberships.ListByPortfolioAsync(portfolioId, cancellationToken).ConfigureAwait(false);
        var entries = new List<PeerVelocityEntry>(memberships.Count);
        foreach (var m in memberships)
        {
            var repo = await _repositories.FindAsync(m.RepositoryId, cancellationToken).ConfigureAwait(false);
            if (repo is null) continue;
            var snapshots = await _snapshots.ListByRepositoryAsync(repo.Id, cancellationToken).ConfigureAwait(false);
            var series = new MetricSeries(snapshots);
            var v = MetricCalculator.ComputeVelocity(series, Domain.Snapshots.MetricNames.Stars, 30, asOfUtc);
            entries.Add(new PeerVelocityEntry(
                repo.Id.Value.ToString(),
                repo.FullName,
                m.Category ?? string.Empty,
                v.AbsoluteChange,
                v.InsufficientReason,
                v.IsNewSignal));
        }
        var byCategory = entries
            .GroupBy(e => string.IsNullOrEmpty(e.Category) ? "(uncategorized)" : e.Category)
            .Select(g => PeerComparator.ComputeCategoryMedian(g.ToList(), g.Key, Domain.Snapshots.MetricNames.Stars, 30, asOfUtc))
            .ToList();
        return new BenchmarkReport(portfolio.Id.Value, "30d", asOfUtc, byCategory);
    }

    private static (int windowDays, DateOnly windowStart, DateOnly windowEnd) ParseWindow(string window, DateTimeOffset asOfUtc)
    {
        return window switch
        {
            "7d" => (7, DateOnly.FromDateTime(asOfUtc.UtcDateTime.AddDays(-7)), DateOnly.FromDateTime(asOfUtc.UtcDateTime)),
            "30d" => (30, DateOnly.FromDateTime(asOfUtc.UtcDateTime.AddDays(-30)), DateOnly.FromDateTime(asOfUtc.UtcDateTime)),
            _ => throw new ArgumentException("window must be '7d' or '30d'.", nameof(window)),
        };
    }

    private static (MetricSnapshot? latest, ProviderResultStatus status, string? diag) LatestSnapshotStatus(IReadOnlyList<MetricSnapshot> snapshots)
    {
        if (snapshots.Count == 0) return (null, ProviderResultStatus.Unavailable, "no-snapshots");
        var byDate = snapshots.GroupBy(s => s.MetricDate).OrderByDescending(g => g.Key).First();
        var latest = byDate.OrderByDescending(s => s.CollectedAtUtc).First();
        var anyUnavailable = byDate.Any(s => s.Status != ProviderResultStatus.Available && s.Status != ProviderResultStatus.Partial);
        var anyPartial = byDate.Any(s => s.Status == ProviderResultStatus.Partial);
        if (anyUnavailable) return (latest, latest.Status, latest.DiagnosticCode);
        if (anyPartial) return (latest, ProviderResultStatus.Partial, latest.DiagnosticCode);
        return (latest, ProviderResultStatus.Available, latest.DiagnosticCode);
    }
}

public sealed record RepositoryMetrics(
    Guid RepositoryId, string NodeId, string OwnerLogin, string Name, RepositoryVisibility Visibility,
    DateOnly? CreatedOnGithubAt, string? PrimaryLanguage,
    DateTimeOffset? LastSnapshotAtUtc, ProviderResultStatus LatestStatus, string? DiagnosticCode,
    IReadOnlyDictionary<string, VelocityResult> Velocities, AccelerationResult? Acceleration,
    EngagementSummary? Engagement);

public sealed record BenchmarkReport(
    Guid PortfolioId, string Window, DateTimeOffset AsOfUtc, IReadOnlyList<PeerBenchmarkResult> Results);
