using Argoscope.Domain.Common;
using Argoscope.Domain.Packages;
using Argoscope.Domain.Repositories;
using Argoscope.Domain.Snapshots;

namespace Argoscope.Application.Packages;

/// <summary>One observation row in the adoption series.</summary>
public sealed record PackageAdoptionPoint(
    DateTimeOffset WindowStartUtc,
    DateTimeOffset WindowEndUtc,
    DateTimeOffset ObservedAtUtc,
    double Value,
    string Status,
    bool IsComplete,
    string? DiagnosticCode);

/// <summary>One provider/unit/window series for a single association.</summary>
public sealed record PackageAdoptionSeries(
    Guid AssociationId,
    string Provider,
    string Coordinate,
    string Unit,
    string Window,
    DateTimeOffset FirstObservedAtUtc,
    DateTimeOffset LastObservedAtUtc,
    int ExpectedPoints,
    int ActualPoints,
    double Coverage,
    string Status,
    IReadOnlyList<PackageAdoptionPoint> Points);

/// <summary>
/// Full adoption report for a repository. Unlike units are never
/// combined: each series is surfaced independently with its own unit,
/// window, freshness and coverage. Missing data is reported as
/// insufficient coverage and a status pill, never as zero.
/// </summary>
public sealed record PackageAdoptionReport(
    Guid RepositoryId,
    DateTimeOffset AsOfUtc,
    IReadOnlyList<PackageAdoptionSeries> Series,
    int SeriesWithData,
    int SeriesStale,
    int SeriesMissing,
    string? InsufficientReason);

/// <summary>
/// Pure read-side aggregator. Groups observations by their
/// (association, unit, window) key and computes per-series freshness
/// and coverage. The expected-points denominator is the number of
/// window steps between the series first-observation and the as-of
/// time, so a series that has been running for 30 days and has 28
/// stored daily observations reports coverage ≈ 0.93.
/// </summary>
public static class PackageAdoptionAggregator
{
    public static PackageAdoptionReport Build(
        Id<Repository> repositoryId,
        IReadOnlyList<PackageAssociation> associations,
        IReadOnlyList<PackageObservation> observations,
        DateTimeOffset asOfUtc)
    {
        var byAssociation = observations
            .GroupBy(o => o.PackageAssociationId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<PackageObservation>)g.ToList());

        var series = new List<PackageAdoptionSeries>();
        var seriesMissing = 0;
        var seriesStale = 0;
        foreach (var association in associations)
        {
            if (association.Status == PackageAssociationStatus.Removed)
            {
                continue;
            }
            byAssociation.TryGetValue(association.Id, out var obs);
            obs ??= Array.Empty<PackageObservation>();

            var groups = obs
                .GroupBy(o => (o.Unit, o.Window))
                .ToList();
            if (groups.Count == 0)
            {
                // Empty series: the association is linked but the
                // provider has not returned any data yet.
                series.Add(new PackageAdoptionSeries(
                    association.Id.Value,
                    association.Provider.ToString(),
                    association.Coordinate,
                    association.DefaultUnit.ToString(),
                    association.DefaultWindow.ToString(),
                    FirstObservedAtUtc: asOfUtc,
                    LastObservedAtUtc: asOfUtc,
                    ExpectedPoints: 0,
                    ActualPoints: 0,
                    Coverage: 0d,
                    Status: association.Status.ToString(),
                    Points: Array.Empty<PackageAdoptionPoint>()));
                seriesMissing++;
                continue;
            }

            foreach (var group in groups)
            {
                var ordered = group.OrderBy(p => p.WindowStartUtc).ToList();
                var first = ordered.First();
                var last = ordered.Last();
                var expected = ExpectedPoints(first.WindowStartUtc, asOfUtc, group.Key.Window);
                var actual = ordered.Count;
                var coverage = expected > 0 ? Math.Min(1d, (double)actual / expected) : 0d;
                var hasNonAvailable = ordered.Any(p => p.Status != ProviderResultStatus.Available);
                var status = association.Status == PackageAssociationStatus.AttentionRequired
                    ? PackageAssociationStatus.AttentionRequired.ToString()
                    : (hasNonAvailable ? "stale" : "ok");
                if (status == "stale") seriesStale++;

                var points = ordered.Select(p => new PackageAdoptionPoint(
                    p.WindowStartUtc, p.WindowEndUtc, p.ObservedAtUtc, p.Value,
                    p.Status.ToString(), p.IsComplete, p.DiagnosticCode)).ToList();
                series.Add(new PackageAdoptionSeries(
                    association.Id.Value,
                    association.Provider.ToString(),
                    association.Coordinate,
                    group.Key.Unit.ToString(),
                    group.Key.Window.ToString(),
                    first.ObservedAtUtc,
                    last.ObservedAtUtc,
                    expected,
                    actual,
                    coverage,
                    status,
                    points));
            }
        }

        var seriesWithData = series.Count(s => s.Points.Count > 0 && s.Status == "ok");
        var insufficient = series.Count == 0
            ? "no-package-associations"
            : (seriesWithData == 0 ? "no-observations" : null);

        return new PackageAdoptionReport(
            repositoryId.Value,
            asOfUtc,
            series,
            seriesWithData,
            seriesStale,
            seriesMissing,
            insufficient);
    }

    private static int ExpectedPoints(DateTimeOffset firstObserved, DateTimeOffset asOfUtc, PackageWindow window)
    {
        if (asOfUtc <= firstObserved) return 0;
        var span = asOfUtc - firstObserved;
        return window switch
        {
            PackageWindow.Cumulative => 1,
            // For daily/weekly/monthly windows, expected points = the
            // number of buckets the first observation would have grown
            // into by asOfUtc, inclusive of both endpoints. For
            // example, a daily series that started 9 days ago and
            // reports up to today has 10 expected points (day 0 ..
            // day 9 inclusive).
            PackageWindow.Daily => Math.Max(1, (int)Math.Floor(span.TotalDays) + 1),
            PackageWindow.Weekly => Math.Max(1, (int)Math.Ceiling(span.TotalDays / 7d)),
            PackageWindow.Monthly => Math.Max(1, (int)Math.Ceiling(span.TotalDays / 30d)),
            _ => 0,
        };
    }
}
