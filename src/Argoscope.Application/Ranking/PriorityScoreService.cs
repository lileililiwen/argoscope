using Argoscope.Application.Benchmarks;
using Argoscope.Application.Collection;
using Argoscope.Application.Common;
using Argoscope.Application.Engagement;
using Argoscope.Application.Metrics;
using Argoscope.Application.Ranking;
using Argoscope.Domain.Common;
using Argoscope.Domain.Memberships;
using Argoscope.Domain.Portfolios;
using Argoscope.Domain.Repositories;
using Argoscope.Domain.Scores;
using Argoscope.Domain.Snapshots;

namespace Argoscope.Application.Ranking;

/// <summary>
/// Read-side service that builds the priority-score ranking from persisted
/// configuration and snapshots. Pure with respect to the supplied stores.
/// </summary>
public sealed class PriorityScoreService
{
    private readonly IPortfolioRepository _portfolios;
    private readonly IRepositoryStore _repositories;
    private readonly IMembershipStore _memberships;
    private readonly IMetricSnapshotStore _snapshots;
    private readonly IEngagementStore _engagement;
    private readonly IScoreConfigurationStore _scoreConfigurations;
    private readonly IClock _clock;

    public PriorityScoreService(
        IPortfolioRepository portfolios,
        IRepositoryStore repositories,
        IMembershipStore memberships,
        IMetricSnapshotStore snapshots,
        IEngagementStore engagement,
        IScoreConfigurationStore scoreConfigurations,
        IClock clock)
    {
        _portfolios = portfolios;
        _repositories = repositories;
        _memberships = memberships;
        _snapshots = snapshots;
        _engagement = engagement;
        _scoreConfigurations = scoreConfigurations;
        _clock = clock;
    }

    public async Task<PriorityScoreRun?> BuildRankingAsync(Id<Portfolio> portfolioId, DateTimeOffset asOfUtc, CancellationToken cancellationToken)
    {
        var portfolio = await _portfolios.FindAsync(portfolioId, cancellationToken).ConfigureAwait(false);
        if (portfolio is null) return null;
        var config = await _scoreConfigurations.GetActiveAsync(portfolioId, cancellationToken).ConfigureAwait(false);
        if (config is null)
        {
            return new PriorityScoreRun(portfolioId.Value.ToString(), "0", Array.Empty<PriorityScore>(), asOfUtc, "no-score-configuration");
        }
        var factors = await BuildRepositoryFactorsAsync(portfolioId, asOfUtc, cancellationToken).ConfigureAwait(false);
        return PriorityScoreCalculator.Compute(config, factors, asOfUtc);
    }

    public async Task<Result<ScoreConfiguration>> UpdateConfigurationAsync(UpdateScoreConfigurationCommand command, CancellationToken cancellationToken)
    {
        var validation = PriorityScoreCalculator.Validate(command.Factors);
        if (!validation.IsSuccess)
        {
            return (Error)validation.Error!;
        }
        var portfolio = await _portfolios.FindAsync(command.PortfolioId, cancellationToken).ConfigureAwait(false);
        if (portfolio is null)
        {
            return Error.NotFound("Portfolio not found.");
        }
        var existing = await _scoreConfigurations.GetActiveAsync(command.PortfolioId, cancellationToken).ConfigureAwait(false);
        var nextVersion = (existing?.Version ?? 0) + 1;
        var config = new ScoreConfiguration(command.PortfolioId, nextVersion, command.Factors, command.Now);
        await _scoreConfigurations.UpsertActiveAsync(config, cancellationToken).ConfigureAwait(false);
        return config;
    }

    public async Task<ScoreConfiguration?> GetConfigurationAsync(Id<Portfolio> portfolioId, CancellationToken cancellationToken) =>
        await _scoreConfigurations.GetActiveAsync(portfolioId, cancellationToken).ConfigureAwait(false);

    public ScoreConfiguration BuildDefaultConfiguration(Id<Portfolio> portfolioId, DateTimeOffset now)
    {
        var factors = FactorNames.BriefDefaults
            .Select(kv => new ScoreFactor(kv.Key, kv.Value, enabled: true))
            .ToList();
        return new ScoreConfiguration(portfolioId, 1, factors, now);
    }

    private async Task<IReadOnlyList<RepositoryFactors>> BuildRepositoryFactorsAsync(Id<Portfolio> portfolioId, DateTimeOffset asOfUtc, CancellationToken cancellationToken)
    {
        var memberships = await _memberships.ListByPortfolioAsync(portfolioId, cancellationToken).ConfigureAwait(false);
        var result = new List<RepositoryFactors>(memberships.Count);
        foreach (var m in memberships)
        {
            var repo = await _repositories.FindAsync(m.RepositoryId, cancellationToken).ConfigureAwait(false);
            if (repo is null) continue;
            var snapshots = await _snapshots.ListByRepositoryAsync(repo.Id, cancellationToken).ConfigureAwait(false);
            var series = new MetricSeries(snapshots);
            var values = new Dictionary<string, double?>(StringComparer.Ordinal);
            var reasons = new Dictionary<string, string?>(StringComparer.Ordinal);
            var starV = MetricCalculator.ComputeVelocity(series, Domain.Snapshots.MetricNames.Stars, 30, asOfUtc);
            var forkV = MetricCalculator.ComputeVelocity(series, Domain.Snapshots.MetricNames.Forks, 30, asOfUtc);
            var contributorV = MetricCalculator.ComputeVelocity(series, Domain.Snapshots.MetricNames.Contributors, 30, asOfUtc);
            // momentum: combined 30d star + fork velocity (normalized later).
            values[FactorNames.Momentum] = (starV.AbsoluteChange ?? 0d) + (forkV.AbsoluteChange ?? 0d);
            reasons[FactorNames.Momentum] = starV.AbsoluteChange is null ? "no-star-window" : null;

            // engagement: total external issues + prs over the last 30 days.
            var engagementBuckets = await _engagement.ListByRepositoryAsync(repo.Id, cancellationToken).ConfigureAwait(false);
            if (engagementBuckets.Count == 0)
            {
                values[FactorNames.Engagement] = 0d;
                reasons[FactorNames.Engagement] = "no-engagement-buckets";
            }
            else
            {
                var start = asOfUtc.UtcDateTime.AddDays(-30).ToDateOnly();
                var end = asOfUtc.UtcDateTime.ToDateOnly();
                var summary = EngagementAggregator.Summarize(new EngagementSeries(engagementBuckets), start, end, asOfUtc);
                values[FactorNames.Engagement] = summary.ExternalIssues + summary.ExternalPullRequests;
                reasons[FactorNames.Engagement] = summary.InsufficientReason;
            }

            // external users: 30d unique external contributors (rough proxy: max of contributor series delta).
            values[FactorNames.ExternalUsers] = contributorV.AbsoluteChange ?? 0d;
            reasons[FactorNames.ExternalUsers] = contributorV.AbsoluteReason();

            // adoption: out of MVP scope; report unavailable.
            values[FactorNames.Adoption] = null;
            reasons[FactorNames.Adoption] = "adoption-not-implemented";

            // commercial: out of MVP scope; report unavailable.
            values[FactorNames.Commercial] = null;
            reasons[FactorNames.Commercial] = "commercial-not-implemented";

            result.Add(new RepositoryFactors(
                repo.Id.Value.ToString(), repo.FullName, values, reasons));
        }
        return result;
    }
}

public interface IScoreConfigurationStore
{
    Task<ScoreConfiguration?> GetActiveAsync(Id<Portfolio> portfolioId, CancellationToken cancellationToken);
    Task UpsertActiveAsync(ScoreConfiguration configuration, CancellationToken cancellationToken);
}

public sealed record UpdateScoreConfigurationCommand(
    Id<Portfolio> PortfolioId,
    IReadOnlyList<ScoreFactor> Factors,
    DateTimeOffset Now);

internal static class VelocityResultExtensions
{
    public static string? AbsoluteReason(this VelocityResult v) => v.InsufficientReason;
}
