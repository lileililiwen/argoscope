namespace Argoscope.Domain.Alerts;

/// <summary>
/// Allowlisted deterministic metric keys an alert rule may reference.
/// Only MVP snapshot/engagement metrics are supported; adoption and
/// AI-derived signals are excluded.
/// </summary>
public static class AlertMetricAllowlist
{
    public const string Stars7d = "stars_7d";
    public const string Stars30d = "stars_30d";
    public const string ExternalEngagement30d = "external_engagement_30d";
    public const string MomentumScore = "momentum_score";
    public const string SnapshotStalenessHours = "snapshot_staleness_hours";

    public static readonly IReadOnlyList<string> All = new[]
    {
        Stars7d, Stars30d, ExternalEngagement30d, MomentumScore, SnapshotStalenessHours,
    };

    public static bool IsSupported(string? metricKey) =>
        metricKey is not null && All.Contains(metricKey, StringComparer.Ordinal);
}
