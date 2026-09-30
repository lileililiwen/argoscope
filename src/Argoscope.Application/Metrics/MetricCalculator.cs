using Argoscope.Domain.Common;
using Argoscope.Domain.Snapshots;

namespace Argoscope.Application.Metrics;

/// <summary>Inputs required to compute a velocity/acceleration.</summary>
public sealed record MetricSeries(IReadOnlyList<MetricSnapshot> Snapshots);

/// <summary>Result of computing a velocity over a window. Null when insufficient.</summary>
public sealed record VelocityResult(
    string MetricName,
    DateOnly? StartDate,
    DateOnly? EndDate,
    double? StartValue,
    double? EndValue,
    double? AbsoluteChange,
    double? PercentChange,
    double? PerDay,
    int ElapsedDays,
    int CoveredDays,
    bool IsNewSignal,
    string? InsufficientReason);

/// <summary>Result of computing a 30-day acceleration.</summary>
public sealed record AccelerationResult(
    string MetricName,
    double? Current30DayChange,
    double? Previous30DayChange,
    double? Acceleration,
    int ElapsedDays,
    int CoveredDays,
    string? InsufficientReason);

/// <summary>
/// Pure metric service. Inputs are immutable snapshots; outputs are computed
/// from the timestamped source observations only. No IO is performed.
/// </summary>
public static class MetricCalculator
{
    /// <summary>
    /// Compute 7/30-day velocity. Window size in days is the
    /// <paramref name="windowDays"/> argument. Endpoint selection is
    /// "latest snapshot as of now" and "latest snapshot at or before
    /// (now - windowDays)" — partial data returns the actual covered days.
    /// </summary>
    public static VelocityResult ComputeVelocity(
        MetricSeries series,
        string metricName,
        int windowDays,
        DateTimeOffset asOfUtc)
    {
        if (windowDays <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(windowDays), windowDays, "Window must be positive.");
        }

        var asOfDate = DateOnly.FromDateTime(asOfUtc.UtcDateTime);
        var ordered = series.Snapshots
            .Where(s => string.Equals(s.MetricName, metricName, StringComparison.Ordinal)
                && s.IsComplete
                && s.Value.HasValue)
            .OrderBy(s => s.MetricDate)
            .ToList();

        if (ordered.Count == 0)
        {
            return new VelocityResult(metricName, null, null, null, null, null, null, null, 0, 0, false, "no-snapshots");
        }

        // Latest complete snapshot at or before asOfDate.
        var latest = ordered.LastOrDefault(s => s.MetricDate <= asOfDate);
        if (latest is null)
        {
            return new VelocityResult(metricName, null, null, null, null, null, null, null, 0, 0, false, "no-latest-snapshot");
        }

        // Latest complete snapshot at or before (asOfDate - windowDays).
        var targetStart = asOfDate.AddDays(-windowDays);
        var baseline = ordered.LastOrDefault(s => s.MetricDate <= targetStart);
        if (baseline is null)
        {
            return new VelocityResult(metricName, null, latest.MetricDate, null, latest.Value, null, null, null, 0, 0, false, "no-baseline-snapshot");
        }

        var absolute = latest.Value!.Value - baseline.Value!.Value;
        var elapsedDays = (latest.MetricDate.DayNumber - baseline.MetricDate.DayNumber);
        var coveredDays = Math.Min(windowDays, elapsedDays);
        var perDay = elapsedDays > 0 ? absolute / elapsedDays : 0d;

        // Percent growth: null when baseline is zero. new_signal true when
        // baseline is zero and current is positive.
        double? percent = null;
        var isNewSignal = false;
        if (baseline.Value == 0d)
        {
            if (latest.Value > 0d)
            {
                isNewSignal = true;
            }
        }
        else
        {
            percent = absolute / baseline.Value!.Value;
        }

        return new VelocityResult(
            metricName,
            baseline.MetricDate,
            latest.MetricDate,
            baseline.Value,
            latest.Value,
            absolute,
            percent,
            perDay,
            elapsedDays,
            coveredDays,
            isNewSignal,
            null);
    }

    /// <summary>
    /// Compute 30-day acceleration as (current 30-day change) minus
    /// (preceding 30-day change). Returns null with a reason if either window
    /// has insufficient coverage.
    /// </summary>
    public static AccelerationResult ComputeAcceleration(
        MetricSeries series,
        string metricName,
        DateTimeOffset asOfUtc)
    {
        const int windowDays = 30;
        var current = ComputeVelocity(series, metricName, windowDays, asOfUtc);
        if (current.InsufficientReason is not null || current.AbsoluteChange is null)
        {
            return new AccelerationResult(metricName, null, null, null, 0, 0, current.InsufficientReason ?? "no-current-window");
        }

        // The preceding window ends the day before the current window starts.
        var previousAsOf = asOfUtc.UtcDateTime.AddDays(-windowDays);
        var previous = ComputeVelocity(series, metricName, windowDays, new DateTimeOffset(previousAsOf, TimeSpan.Zero));
        if (previous.InsufficientReason is not null || previous.AbsoluteChange is null)
        {
            return new AccelerationResult(metricName, current.AbsoluteChange, null, null, current.ElapsedDays, current.CoveredDays, "no-previous-window");
        }

        return new AccelerationResult(
            metricName,
            current.AbsoluteChange,
            previous.AbsoluteChange,
            current.AbsoluteChange - previous.AbsoluteChange,
            current.ElapsedDays,
            current.CoveredDays,
            null);
    }
}
