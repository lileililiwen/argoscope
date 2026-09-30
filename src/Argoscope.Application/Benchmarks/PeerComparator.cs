using Argoscope.Application.Metrics;

namespace Argoscope.Application.Benchmarks;

/// <summary>Inputs for a peer benchmark: one velocity per comparable repository.</summary>
public sealed record PeerVelocityEntry(
    string RepositoryId,
    string RepositoryLabel,
    string Category,
    double? Velocity30Day,
    string? InsufficientReason,
    bool IsNewSignal);

/// <summary>Median result for a category metric.</summary>
public sealed record PeerBenchmarkResult(
    string Category,
    string MetricName,
    int WindowDays,
    int CohortSize,
    int EligibleSize,
    double? Median,
    DateTimeOffset AsOfUtc,
    string? InsufficientReason);

/// <summary>
/// Pure peer comparison. Median is reported only when at least five
/// comparable repositories have a numeric value for the metric; otherwise
/// the result is null and the cohort size is exposed for transparency.
/// </summary>
public static class PeerComparator
{
    public const int MinCohortSize = 5;

    public static PeerBenchmarkResult ComputeCategoryMedian(
        IReadOnlyList<PeerVelocityEntry> entries,
        string category,
        string metricName,
        int windowDays,
        DateTimeOffset asOfUtc)
    {
        var inCategory = entries
            .Where(e => string.Equals(e.Category, category, StringComparison.Ordinal))
            .ToList();

        if (inCategory.Count < MinCohortSize)
        {
            return new PeerBenchmarkResult(category, metricName, windowDays, inCategory.Count, inCategory.Count, null, asOfUtc,
                $"cohort-too-small:{inCategory.Count}<{MinCohortSize}");
        }

        var eligible = inCategory
            .Where(e => e.Velocity30Day.HasValue && !e.IsNewSignal)
            .ToList();

        if (eligible.Count < MinCohortSize)
        {
            return new PeerBenchmarkResult(category, metricName, windowDays, inCategory.Count, eligible.Count, null, asOfUtc,
                $"eligible-cohort-too-small:{eligible.Count}<{MinCohortSize}");
        }

        var sorted = eligible.Select(e => e.Velocity30Day!.Value).OrderBy(v => v).ToList();
        var mid = sorted.Count / 2;
        double median;
        if (sorted.Count % 2 == 0)
        {
            median = (sorted[mid - 1] + sorted[mid]) / 2d;
        }
        else
        {
            median = sorted[mid];
        }

        return new PeerBenchmarkResult(category, metricName, windowDays, inCategory.Count, eligible.Count, median, asOfUtc, null);
    }
}
