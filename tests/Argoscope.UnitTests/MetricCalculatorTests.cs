using Argoscope.Application.Metrics;
using Argoscope.Domain.Common;
using Argoscope.Domain.Repositories;
using Argoscope.Domain.Snapshots;
using Xunit;

namespace Argoscope.UnitTests;

public class MetricCalculatorTests
{
    private static MetricSnapshot Snap(Id<Repository> repo, string name, DateOnly date, double value, ProviderResultStatus status = ProviderResultStatus.Available) =>
        new(repo, name, date, "test-1", value, status, isComplete: value >= 0, observedAtUtc: date.ToDateTime(TimeOnly.MinValue), collectedAtUtc: date.ToDateTime(TimeOnly.MinValue));

    [Fact]
    public void ComputeVelocity_returns_complete_window_with_endpoints_and_per_day()
    {
        var repoId = Id<Repository>.New();
        var series = new MetricSeries(new[]
        {
            Snap(repoId, "stars", new DateOnly(2024, 1, 1), 100d),
            Snap(repoId, "stars", new DateOnly(2024, 1, 8), 114d),
            Snap(repoId, "stars", new DateOnly(2024, 1, 15), 130d),
        });
        var asOf = new DateTimeOffset(2024, 1, 15, 0, 0, 0, TimeSpan.Zero);
        var v = MetricCalculator.ComputeVelocity(series, "stars", 7, asOf);
        Assert.Equal(new DateOnly(2024, 1, 8), v.StartDate);
        Assert.Equal(new DateOnly(2024, 1, 15), v.EndDate);
        Assert.Equal(114d, v.StartValue);
        Assert.Equal(130d, v.EndValue);
        Assert.Equal(16d, v.AbsoluteChange);
        Assert.Equal(7, v.ElapsedDays);
        Assert.Equal(16d / 7d, v.PerDay!.Value, 6);
        Assert.Null(v.InsufficientReason);
    }

    [Fact]
    public void ComputeVelocity_returns_null_when_baseline_missing()
    {
        var repoId = Id<Repository>.New();
        var series = new MetricSeries(new[]
        {
            Snap(repoId, "stars", new DateOnly(2024, 1, 14), 110d),
            Snap(repoId, "stars", new DateOnly(2024, 1, 15), 120d),
        });
        var asOf = new DateTimeOffset(2024, 1, 15, 0, 0, 0, TimeSpan.Zero);
        var v = MetricCalculator.ComputeVelocity(series, "stars", 7, asOf);
        Assert.Null(v.StartDate);
        Assert.Equal(new DateOnly(2024, 1, 15), v.EndDate);
        Assert.Equal(120d, v.EndValue);
        Assert.Equal("no-baseline-snapshot", v.InsufficientReason);
    }

    [Fact]
    public void ComputeVelocity_zero_baseline_returns_null_percent_and_new_signal()
    {
        var repoId = Id<Repository>.New();
        var series = new MetricSeries(new[]
        {
            Snap(repoId, "stars", new DateOnly(2024, 1, 1), 0d),
            Snap(repoId, "stars", new DateOnly(2024, 1, 8), 5d),
        });
        var asOf = new DateTimeOffset(2024, 1, 8, 0, 0, 0, TimeSpan.Zero);
        var v = MetricCalculator.ComputeVelocity(series, "stars", 7, asOf);
        Assert.Null(v.PercentChange);
        Assert.True(v.IsNewSignal);
    }

    [Fact]
    public void ComputeVelocity_zero_baseline_zero_current_is_not_new_signal()
    {
        var repoId = Id<Repository>.New();
        var series = new MetricSeries(new[]
        {
            Snap(repoId, "stars", new DateOnly(2024, 1, 1), 0d),
            Snap(repoId, "stars", new DateOnly(2024, 1, 8), 0d),
        });
        var asOf = new DateTimeOffset(2024, 1, 8, 0, 0, 0, TimeSpan.Zero);
        var v = MetricCalculator.ComputeVelocity(series, "stars", 7, asOf);
        Assert.Null(v.PercentChange);
        Assert.False(v.IsNewSignal);
    }

    [Fact]
    public void ComputeVelocity_incomplete_snapshots_excluded()
    {
        var repoId = Id<Repository>.New();
        var series = new MetricSeries(new[]
        {
            new MetricSnapshot(repoId, "stars", new DateOnly(2024, 1, 1), "v1", 100d, ProviderResultStatus.Partial, isComplete: false, observedAtUtc: default, collectedAtUtc: default),
            Snap(repoId, "stars", new DateOnly(2024, 1, 8), 130d),
        });
        var asOf = new DateTimeOffset(2024, 1, 8, 0, 0, 0, TimeSpan.Zero);
        var v = MetricCalculator.ComputeVelocity(series, "stars", 7, asOf);
        Assert.Equal("no-baseline-snapshot", v.InsufficientReason);
    }

    [Fact]
    public void ComputeAcceleration_returns_null_when_previous_window_missing()
    {
        var repoId = Id<Repository>.New();
        var series = new MetricSeries(new[]
        {
            Snap(repoId, "stars", new DateOnly(2024, 1, 16), 200d),
            Snap(repoId, "stars", new DateOnly(2024, 2, 15), 220d),
        });
        var asOf = new DateTimeOffset(2024, 2, 15, 0, 0, 0, TimeSpan.Zero);
        var a = MetricCalculator.ComputeAcceleration(series, "stars", asOf);
        Assert.Equal("no-previous-window", a.InsufficientReason);
    }

    [Fact]
    public void ComputeAcceleration_returns_difference_when_both_windows_present()
    {
        var repoId = Id<Repository>.New();
        var series = new MetricSeries(new[]
        {
            Snap(repoId, "stars", new DateOnly(2024, 1, 15), 50d),
            Snap(repoId, "stars", new DateOnly(2024, 2, 14), 70d),
            Snap(repoId, "stars", new DateOnly(2024, 3, 15), 100d),
        });
        var asOf = new DateTimeOffset(2024, 3, 15, 0, 0, 0, TimeSpan.Zero);
        var a = MetricCalculator.ComputeAcceleration(series, "stars", asOf);
        Assert.Equal(30d, a.Current30DayChange);
        Assert.Equal(20d, a.Previous30DayChange);
        Assert.Equal(10d, a.Acceleration);
    }
}
