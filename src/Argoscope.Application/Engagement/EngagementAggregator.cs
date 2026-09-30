using Argoscope.Domain.Engagement;

namespace Argoscope.Application.Engagement;

/// <summary>Windowed engagement series for a single repository.</summary>
public sealed record EngagementSeries(IReadOnlyList<EngagementBucket> Buckets);

/// <summary>Aggregated engagement totals over a window, split by attribution.</summary>
public sealed record EngagementSummary(
    DateOnly WindowStart,
    DateOnly WindowEnd,
    int ExternalIssues,
    int ExternalPullRequests,
    int ExternalContributors,
    int OwnerIssues,
    int OwnerPullRequests,
    int OwnerContributors,
    int UnknownIssues,
    int UnknownPullRequests,
    int UnknownContributors,
    DateTimeOffset AsOfUtc,
    int CoveredDays,
    string? InsufficientReason);

/// <summary>
/// Pure engagement aggregation. Sums the daily buckets in the window;
/// missing buckets are treated as zero for the day only when an empty bucket
/// was explicitly recorded; otherwise the day contributes zero and the
/// covered-days count still reflects the requested window.
/// </summary>
public static class EngagementAggregator
{
    public static EngagementSummary Summarize(
        EngagementSeries series,
        DateOnly windowStart,
        DateOnly windowEnd,
        DateTimeOffset asOfUtc)
    {
        if (windowEnd < windowStart)
        {
            throw new ArgumentException("windowEnd must be on or after windowStart.", nameof(windowEnd));
        }

        var inWindow = series.Buckets
            .Where(b => b.BucketDate >= windowStart && b.BucketDate <= windowEnd)
            .ToList();

        if (inWindow.Count == 0)
        {
            return new EngagementSummary(
                windowStart, windowEnd,
                0, 0, 0,
                0, 0, 0,
                0, 0, 0,
                asOfUtc,
                0,
                "no-engagement-snapshots");
        }

        return new EngagementSummary(
            windowStart, windowEnd,
            inWindow.Sum(b => b.ExternalIssuesOpened),
            inWindow.Sum(b => b.ExternalPullRequestsOpened),
            inWindow.Sum(b => b.ExternalContributors),
            inWindow.Sum(b => b.OwnerIssuesOpened),
            inWindow.Sum(b => b.OwnerPullRequestsOpened),
            inWindow.Sum(b => b.OwnerContributors),
            inWindow.Sum(b => b.UnknownIssuesOpened),
            inWindow.Sum(b => b.UnknownPullRequestsOpened),
            inWindow.Sum(b => b.UnknownContributors),
            asOfUtc,
            (windowEnd.DayNumber - windowStart.DayNumber) + 1,
            null);
    }
}
