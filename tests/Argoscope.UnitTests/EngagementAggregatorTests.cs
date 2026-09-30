using Argoscope.Application.Engagement;
using Argoscope.Domain.Common;
using Argoscope.Domain.Engagement;
using Argoscope.Domain.Repositories;
using Xunit;

namespace Argoscope.UnitTests;

public class EngagementAggregatorTests
{
    [Fact]
    public void Summarize_sums_buckets_and_splits_attribution()
    {
        var repoId = Id<Repository>.New();
        var buckets = new[]
        {
            new EngagementBucket(repoId, new DateOnly(2024, 1, 1),
                1, 1, 2,
                0, 0, 1,
                0, 0, 0,
                default, default),
            new EngagementBucket(repoId, new DateOnly(2024, 1, 2),
                2, 0, 1,
                1, 0, 0,
                0, 0, 1,
                default, default),
        };
        var summary = EngagementAggregator.Summarize(new EngagementSeries(buckets),
            new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 2), default);
        Assert.Equal(3, summary.ExternalIssues);
        Assert.Equal(1, summary.ExternalPullRequests);
        Assert.Equal(3, summary.ExternalContributors);
        Assert.Equal(1, summary.OwnerIssues);
        Assert.Equal(1, summary.OwnerContributors);
        Assert.Equal(1, summary.UnknownContributors);
        Assert.Equal(2, summary.CoveredDays);
        Assert.Null(summary.InsufficientReason);
    }

    [Fact]
    public void Summarize_empty_series_returns_no_engagement_snapshots()
    {
        var summary = EngagementAggregator.Summarize(new EngagementSeries(Array.Empty<EngagementBucket>()),
            new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 7), default);
        Assert.Equal("no-engagement-snapshots", summary.InsufficientReason);
        Assert.Equal(0, summary.ExternalIssues);
    }
}
