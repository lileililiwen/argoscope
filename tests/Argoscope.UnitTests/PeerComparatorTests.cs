using Argoscope.Application.Benchmarks;
using Xunit;

namespace Argoscope.UnitTests;

public class PeerComparatorTests
{
    [Fact]
    public void Median_returned_only_when_at_least_five_comparable_repositories()
    {
        var entries = new[]
        {
            new PeerVelocityEntry("a", "a/a", "ci", 1d, null, false),
            new PeerVelocityEntry("b", "b/b", "ci", 2d, null, false),
            new PeerVelocityEntry("c", "c/c", "ci", 3d, null, false),
            new PeerVelocityEntry("d", "d/d", "ci", 4d, null, false),
            new PeerVelocityEntry("e", "e/e", "ci", 5d, null, false),
        };
        var r = PeerComparator.ComputeCategoryMedian(entries, "ci", "stars", 30, default);
        Assert.Equal(5, r.CohortSize);
        Assert.Equal(5, r.EligibleSize);
        Assert.Equal(3d, r.Median);
        Assert.Null(r.InsufficientReason);
    }

    [Fact]
    public void Median_null_when_fewer_than_five_repositories()
    {
        var entries = new[]
        {
            new PeerVelocityEntry("a", "a/a", "ci", 1d, null, false),
            new PeerVelocityEntry("b", "b/b", "ci", 2d, null, false),
            new PeerVelocityEntry("c", "c/c", "ci", 3d, null, false),
            new PeerVelocityEntry("d", "d/d", "ci", 4d, null, false),
        };
        var r = PeerComparator.ComputeCategoryMedian(entries, "ci", "stars", 30, default);
        Assert.Equal(4, r.CohortSize);
        Assert.Null(r.Median);
        Assert.Contains("cohort-too-small", r.InsufficientReason);
    }

    [Fact]
    public void Median_excludes_new_signal_repositories_from_eligible_count()
    {
        var entries = new[]
        {
            new PeerVelocityEntry("a", "a/a", "ci", 1d, null, false),
            new PeerVelocityEntry("b", "b/b", "ci", 2d, null, false),
            new PeerVelocityEntry("c", "c/c", "ci", 3d, null, false),
            new PeerVelocityEntry("d", "d/d", "ci", 4d, null, false),
            new PeerVelocityEntry("e", "e/e", "ci", 5d, null, true), // new-signal
        };
        var r = PeerComparator.ComputeCategoryMedian(entries, "ci", "stars", 30, default);
        Assert.Equal(5, r.CohortSize);
        Assert.Equal(4, r.EligibleSize);
        Assert.Null(r.Median);
        Assert.Contains("eligible-cohort-too-small", r.InsufficientReason);
    }
}
