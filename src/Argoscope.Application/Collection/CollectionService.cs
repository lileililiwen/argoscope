using Argoscope.Domain.Common;
using Argoscope.Domain.Repositories;
using Argoscope.Domain.Snapshots;
using Argoscope.GitHub;
using Microsoft.Extensions.Logging;

namespace Argoscope.Application.Collection;

/// <summary>
/// Orchestrates one provider call per (portfolio, repository, day). It does
/// not mutate GitHub; it only writes snapshots, engagement buckets and the
/// checkpoint. Last-good values are preserved: a partial provider response
/// updates only the fields the provider returned, and missing fields keep
/// their previous value (the application never overwrites a verified
/// snapshot with zero).
/// </summary>
public sealed class CollectionService
{
    private readonly IGitHubRepositoryProvider _provider;
    private readonly IRepositoryStore _repositories;
    private readonly IMetricSnapshotStore _snapshots;
    private readonly IEngagementStore _engagement;
    private readonly ICheckpointStore _checkpoints;
    private readonly ILogger<CollectionService> _logger;
    private readonly IClock _clock;

    public CollectionService(
        IGitHubRepositoryProvider provider,
        IRepositoryStore repositories,
        IMetricSnapshotStore snapshots,
        IEngagementStore engagement,
        ICheckpointStore checkpoints,
        ILogger<CollectionService> logger,
        IClock clock)
    {
        _provider = provider;
        _repositories = repositories;
        _snapshots = snapshots;
        _engagement = engagement;
        _checkpoints = checkpoints;
        _logger = logger;
        _clock = clock;
    }

    public async Task<CollectionRunResult> RunAsync(CollectionRequest request, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;

        // 1. Resolve the repository identity (or create one if the provider returns a new node id).
        var repositoryResult = await _provider.GetRepositoryAsync(request.OwnerLogin, request.Name, cancellationToken).ConfigureAwait(false);
        var repoStatus = repositoryResult.Status;
        Repository? repository = null;
        if (repositoryResult.Status == ProviderResultStatus.Available && repositoryResult.Value is not null)
        {
            var obs = repositoryResult.Value;
            repository = await _repositories.FindByNodeIdAsync(obs.NodeId, cancellationToken).ConfigureAwait(false);
            if (repository is null)
            {
                repository = new Repository(
                    obs.NodeId, obs.OwnerLogin, obs.Name, obs.Visibility, now,
                    obs.CreatedOnGithubAt, obs.PrimaryLanguage, obs.LastActivityAtUtc);
                await _repositories.AddAsync(repository, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                repository.UpdateLocator(obs.OwnerLogin, obs.Name, obs.Visibility, now,
                    obs.CreatedOnGithubAt, obs.PrimaryLanguage, obs.LastActivityAtUtc);
                await _repositories.UpdateLocatorAsync(repository, cancellationToken).ConfigureAwait(false);
            }
        }
        else
        {
            // The repository may already exist by locator; use it so the checkpoint can record the failure against the correct row.
            repository = await _repositories.FindByLocatorAsync(request.OwnerLogin, request.Name, cancellationToken).ConfigureAwait(false);
        }

        if (repository is null)
        {
            // No repository yet (404 etc.) - we can't write a snapshot row because the FK requires a known repo.
            // The caller will observe this in the run result and decide whether to remove the membership.
            return new CollectionRunResult(
                default,
                0,
                0,
                repoStatus,
                ProviderResultStatus.Unavailable,
                ProviderResultStatus.Unavailable,
                now);
        }

        // 2. Get or create the checkpoint for this provider version.
        var checkpoint = await _checkpoints.GetOrCreateAsync(
            request.PortfolioId, repository.Id, _provider.ProviderVersion, now, cancellationToken).ConfigureAwait(false);

        if (repoStatus is ProviderResultStatus.RateLimited
            or ProviderResultStatus.Unauthorized
            or ProviderResultStatus.Forbidden
            or ProviderResultStatus.NotFound
            or ProviderResultStatus.Unavailable
            or ProviderResultStatus.Malformed)
        {
            checkpoint.RecordFailure(repoStatus, checkpoint.Cursor, repositoryResult.RetryAfterUtc, now);
            await _checkpoints.UpdateAsync(checkpoint, cancellationToken).ConfigureAwait(false);
            return new CollectionRunResult(repository.Id, 0, 0, repoStatus, ProviderResultStatus.Unavailable, ProviderResultStatus.Unavailable, now);
        }

        // 3. Walk the metric pages, idempotently writing one snapshot per (repo, metric, day, provider version).
        int snapshotsWritten = 0;
        var anyMetricPartial = false;
        var anyMetricFailure = false;
        var lastMetricCursor = checkpoint.Cursor;
        foreach (var metricName in SnapshotMetricCatalog.AllowedForRepositoryStatus(repoStatus))
        {
            string? cursor = null;
            var pageCount = 0;
            var maxPages = 100;
            while (pageCount < maxPages)
            {
                var page = await _provider.GetMetricPageAsync(request.OwnerLogin, request.Name, metricName, cursor, request.MetricDate.AddDays(-365), cancellationToken).ConfigureAwait(false);
                if (page.Status != ProviderResultStatus.Available && page.Status != ProviderResultStatus.Partial)
                {
                    anyMetricFailure = true;
                    break;
                }
                if (page.Items.Count == 0)
                {
                    break;
                }
                foreach (var obs in page.Items)
                {
                    var existing = await _snapshots.FindAsync(repository.Id, obs.MetricName, obs.MetricDate, _provider.ProviderVersion, cancellationToken).ConfigureAwait(false);
                    var isComplete = obs.Value.HasValue;
                    if (existing is not null)
                    {
                        // Do not overwrite a previously verified value with null/missing; only refresh complete observations.
                        if (!isComplete)
                        {
                            continue;
                        }
                    }
                    var snapshot = new MetricSnapshot(
                        repository.Id, obs.MetricName, obs.MetricDate, _provider.ProviderVersion,
                        obs.Value, page.Status, isComplete, obs.ObservedAtUtc, now, page.DiagnosticCode);
                    await _snapshots.UpsertAsync(snapshot, cancellationToken).ConfigureAwait(false);
                    snapshotsWritten++;
                }
                if (page.Status == ProviderResultStatus.Partial)
                {
                    anyMetricPartial = true;
                }
                cursor = page.NextCursor;
                lastMetricCursor = cursor;
                pageCount++;
                if (cursor is null)
                {
                    break;
                }
            }
        }

        // 4. Walk engagement pages, one bucket per day.
        int engagementWritten = 0;
        string? lastEngagementCursor = null;
        var pageCount2 = 0;
        var maxPages2 = 100;
        while (pageCount2 < maxPages2)
        {
            var page = await _provider.GetEngagementPageAsync(request.OwnerLogin, request.Name, null, now.AddDays(-365), cancellationToken).ConfigureAwait(false);
            if (page.Status != ProviderResultStatus.Available && page.Status != ProviderResultStatus.Partial)
            {
                anyMetricFailure = true;
                break;
            }
            if (page.Items.Count == 0)
            {
                break;
            }
            foreach (var obs in page.Items)
            {
                var existing = await _engagement.FindAsync(repository.Id, obs.BucketDate, cancellationToken).ConfigureAwait(false);
                if (existing is not null)
                {
                    // Engagement bucket is a per-day rollup, not a counter: refresh in place when complete.
                    if (obs.ExternalIssuesOpened + obs.ExternalPullRequestsOpened + obs.ExternalContributors
                        + obs.OwnerIssuesOpened + obs.OwnerPullRequestsOpened + obs.OwnerContributors
                        + obs.UnknownIssuesOpened + obs.UnknownPullRequestsOpened + obs.UnknownContributors == 0)
                    {
                        continue;
                    }
                }
                var bucket = new Domain.Engagement.EngagementBucket(
                    repository.Id, obs.BucketDate,
                    obs.ExternalIssuesOpened, obs.ExternalPullRequestsOpened, obs.ExternalContributors,
                    obs.OwnerIssuesOpened, obs.OwnerPullRequestsOpened, obs.OwnerContributors,
                    obs.UnknownIssuesOpened, obs.UnknownPullRequestsOpened, obs.UnknownContributors,
                    obs.ObservedAtUtc, now);
                await _engagement.UpsertAsync(bucket, cancellationToken).ConfigureAwait(false);
                engagementWritten++;
            }
            lastEngagementCursor = page.NextCursor;
            pageCount2++;
            if (page.NextCursor is null)
            {
                break;
            }
        }

        // 5. Update the checkpoint.
        var metricsStatus = anyMetricFailure ? ProviderResultStatus.Unavailable : (anyMetricPartial ? ProviderResultStatus.Partial : ProviderResultStatus.Available);
        if (metricsStatus == ProviderResultStatus.Available)
        {
            checkpoint.RecordSuccess(lastMetricCursor, now);
        }
        else
        {
            checkpoint.RecordPartial(lastMetricCursor, now);
        }
        await _checkpoints.UpdateAsync(checkpoint, cancellationToken).ConfigureAwait(false);
        _ = lastEngagementCursor; // engagement cursor is currently not persisted (one page only); see D3 follow-up.

        return new CollectionRunResult(
            repository.Id, snapshotsWritten, engagementWritten, repoStatus, metricsStatus, metricsStatus, now);
    }
}

/// <summary>
/// Static list of metric names the collection service iterates. Kept in the
/// application layer because it is policy (what we collect), not persistence.
/// </summary>
public static class SnapshotMetricCatalog
{
    public static readonly IReadOnlyList<string> DailyMetrics = new[]
    {
        Domain.Snapshots.MetricNames.Stars,
        Domain.Snapshots.MetricNames.Forks,
        Domain.Snapshots.MetricNames.Watchers,
        Domain.Snapshots.MetricNames.OpenIssues,
        Domain.Snapshots.MetricNames.OpenPullRequests,
        Domain.Snapshots.MetricNames.Contributors,
        Domain.Snapshots.MetricNames.ReleasesLastYear,
        Domain.Snapshots.MetricNames.CommitsLast30Days,
    };

    public static IReadOnlyList<string> AllowedForRepositoryStatus(ProviderResultStatus status) =>
        status == ProviderResultStatus.Available || status == ProviderResultStatus.Partial
            ? DailyMetrics
            : Array.Empty<string>();
}
