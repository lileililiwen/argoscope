using Argoscope.Application.Packages;
using Argoscope.Domain.Common;
using Argoscope.Domain.Packages;
using Argoscope.Domain.Repositories;
using Argoscope.Domain.Snapshots;
using Xunit;

namespace Argoscope.UnitTests;

public class PackageAdoptionAggregatorTests
{
    private static readonly Id<Repository> RepoId = Id<Repository>.From(Guid.NewGuid());
    private static readonly Id<PackageAssociation> AssociationId = Id<PackageAssociation>.From(Guid.NewGuid());

    [Fact]
    public void Empty_associations_yield_insufficient_no_associations()
    {
        var asOf = new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.Zero);
        var report = PackageAdoptionAggregator.Build(RepoId, Array.Empty<PackageAssociation>(), Array.Empty<PackageObservation>(), asOf);
        Assert.Empty(report.Series);
        Assert.Equal(0, report.SeriesWithData);
        Assert.Equal("no-package-associations", report.InsufficientReason);
    }

    [Fact]
    public void Linked_association_without_observations_yields_empty_series()
    {
        var asOf = new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.Zero);
        var assoc = new PackageAssociation(RepoId, PackageProvider.DockerHub, "owner/repo",
            PackageUnit.Pulls, PackageWindow.Cumulative, asOf);
        var report = PackageAdoptionAggregator.Build(RepoId, new[] { assoc }, Array.Empty<PackageObservation>(), asOf);
        Assert.Single(report.Series);
        var series = report.Series[0];
        Assert.Equal("DockerHub", series.Provider);
        Assert.Equal("Pulls", series.Unit);
        Assert.Equal(0, series.ActualPoints);
        Assert.Equal(1, report.SeriesMissing);
        Assert.Equal("no-observations", report.InsufficientReason);
    }

    [Fact]
    public void Groups_observations_by_unit_and_window_into_separate_series()
    {
        var asOf = new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.Zero);
        var start = asOf.AddDays(-29);
        var assoc = new PackageAssociation(RepoId, PackageProvider.Npm, "@argoscope/sample",
            PackageUnit.Downloads, PackageWindow.Weekly, asOf);

        // Two unit/window series for the same association: Weekly
        // downloads and Daily downloads. The aggregator must keep
        // them separate.
        var weekly = new PackageObservation(assoc.Id, PackageProvider.Npm, "fake-npm-1",
            PackageUnit.Downloads, PackageWindow.Weekly,
            start, asOf, 1200, ProviderResultStatus.Available, true, start, asOf, null);
        var dailyA = new PackageObservation(assoc.Id, PackageProvider.Npm, "fake-npm-1",
            PackageUnit.Downloads, PackageWindow.Daily,
            asOf.AddDays(-2), asOf.AddDays(-1), 50, ProviderResultStatus.Available, true, asOf.AddDays(-2), asOf, null);
        var dailyB = new PackageObservation(assoc.Id, PackageProvider.Npm, "fake-npm-1",
            PackageUnit.Downloads, PackageWindow.Daily,
            asOf.AddDays(-1), asOf, 60, ProviderResultStatus.Available, true, asOf.AddDays(-1), asOf, null);

        var report = PackageAdoptionAggregator.Build(RepoId, new[] { assoc }, new[] { weekly, dailyA, dailyB }, asOf);

        Assert.Equal(2, report.Series.Count);
        var seriesByKey = report.Series.ToDictionary(s => (s.Unit, s.Window));
        Assert.True(seriesByKey.ContainsKey(("Downloads", "Weekly")));
        Assert.True(seriesByKey.ContainsKey(("Downloads", "Daily")));
        Assert.Single(seriesByKey[("Downloads", "Weekly")].Points);
        Assert.Equal(2, seriesByKey[("Downloads", "Daily")].Points.Count);
        // The two series must not have been merged.
        Assert.Equal(2, report.SeriesWithData);
    }

    [Fact]
    public void Provider_failure_marks_series_stale_but_keeps_points()
    {
        var asOf = new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.Zero);
        var start = asOf.AddDays(-10);
        var assoc = new PackageAssociation(RepoId, PackageProvider.PyPI, "argoscope-sample",
            PackageUnit.Downloads, PackageWindow.Daily, asOf);
        var partialObs = new PackageObservation(assoc.Id, PackageProvider.PyPI, "fake-pypi-1",
            PackageUnit.Downloads, PackageWindow.Daily,
            start, start.AddDays(1), 100, ProviderResultStatus.Partial, true, start, asOf, "rate-limited");

        var report = PackageAdoptionAggregator.Build(RepoId, new[] { assoc }, new[] { partialObs }, asOf);

        Assert.Single(report.Series);
        Assert.Equal("stale", report.Series[0].Status);
        Assert.Equal(1, report.SeriesStale);
        Assert.Single(report.Series[0].Points);
        Assert.Equal("rate-limited", report.Series[0].Points[0].DiagnosticCode);
    }

    [Fact]
    public void Attention_required_association_surfaces_attention_status()
    {
        var asOf = new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.Zero);
        var assoc = new PackageAssociation(RepoId, PackageProvider.NuGet, "Argoscope.Sample",
            PackageUnit.Downloads, PackageWindow.Cumulative, asOf);
        assoc.MarkAttentionRequired("package-not-found", asOf);

        var report = PackageAdoptionAggregator.Build(RepoId, new[] { assoc }, Array.Empty<PackageObservation>(), asOf);

        Assert.Single(report.Series);
        Assert.Equal("AttentionRequired", report.Series[0].Status);
    }

    [Fact]
    public void Removed_associations_are_excluded()
    {
        var asOf = new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.Zero);
        var removed = new PackageAssociation(RepoId, PackageProvider.Npm, "@argoscope/sample",
            PackageUnit.Downloads, PackageWindow.Weekly, asOf);
        removed.MarkRemoved(asOf);
        var report = PackageAdoptionAggregator.Build(RepoId, new[] { removed }, Array.Empty<PackageObservation>(), asOf);
        Assert.Empty(report.Series);
    }

    [Fact]
    public void Coverage_uses_expected_points_for_window()
    {
        var asOf = new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.Zero);
        var assoc = new PackageAssociation(RepoId, PackageProvider.PyPI, "argoscope-sample",
            PackageUnit.Downloads, PackageWindow.Daily, asOf);
        // 10 daily observations over the last 10 days → coverage should
        // be capped at 1.0 because expected is 10.
        var observations = new List<PackageObservation>();
        for (var i = 0; i < 10; i++)
        {
            var day = asOf.AddDays(-9 + i);
            observations.Add(new PackageObservation(
                assoc.Id, PackageProvider.PyPI, "fake-pypi-1",
                PackageUnit.Downloads, PackageWindow.Daily,
                day, day.AddDays(1), i * 10, ProviderResultStatus.Available, true, day, asOf, null));
        }
        var report = PackageAdoptionAggregator.Build(RepoId, new[] { assoc }, observations, asOf);
        Assert.Single(report.Series);
        Assert.Equal(10, report.Series[0].ActualPoints);
        Assert.Equal(10, report.Series[0].ExpectedPoints);
        Assert.Equal(1d, report.Series[0].Coverage);
    }
}
