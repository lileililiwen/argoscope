using Argoscope.Domain.Common;
using Argoscope.Domain.Packages;
using Argoscope.Domain.Snapshots;

namespace Argoscope.Packages;

/// <summary>
/// One provider-reported adoption observation. The provider returns a
/// stream of these for each call; the collection service turns each into
/// a <see cref="Domain.Packages.PackageObservation"/>. Unlike
/// <see cref="GitHub.MetricObservation"/>, an observation here carries
/// its own unit and window because different registries report
/// different units and windows — combining them would be a category
/// error and is rejected by the read model.
/// </summary>
public sealed record PackageMetricObservation(
    PackageProvider Provider,
    PackageUnit Unit,
    PackageWindow Window,
    DateTimeOffset WindowStartUtc,
    DateTimeOffset WindowEndUtc,
    double Value,
    DateTimeOffset ObservedAtUtc);

/// <summary>One page of a paginated package provider call.</summary>
public sealed record PackageMetricPage(
    IReadOnlyList<PackageMetricObservation> Items,
    string? NextCursor,
    ProviderResultStatus Status,
    DateTimeOffset? RetryAfterUtc,
    string? DiagnosticCode);

/// <summary>
/// Single-value provider result used for the repository-existence probe
/// (does this coordinate resolve at the registry?). The collection
/// service does not require this to succeed to write observations: a
/// missing package keeps the association in <see
/// cref="PackageAssociationStatus.AttentionRequired"/> and preserves
/// last-good observations.
/// </summary>
public readonly struct PackageMetadataResult
{
    public bool Found { get; }
    public DateTimeOffset? ObservedAtUtc { get; }
    public ProviderResultStatus Status { get; }
    public DateTimeOffset? RetryAfterUtc { get; }
    public string? DiagnosticCode { get; }

    private PackageMetadataResult(bool found, DateTimeOffset? observedAtUtc, ProviderResultStatus status, DateTimeOffset? retryAfterUtc, string? diagnosticCode)
    {
        Found = found;
        ObservedAtUtc = observedAtUtc;
        Status = status;
        RetryAfterUtc = retryAfterUtc;
        DiagnosticCode = diagnosticCode;
    }

    public static PackageMetadataResult Ok(DateTimeOffset observedAtUtc) =>
        new(true, observedAtUtc, ProviderResultStatus.Available, null, null);

    public static PackageMetadataResult NotFound(string code) =>
        new(false, null, ProviderResultStatus.NotFound, null, code);

    public static PackageMetadataResult Failure(ProviderResultStatus status, string code, DateTimeOffset? retryAfter = null) =>
        new(false, null, status, retryAfter, code);
}

/// <summary>
/// Provider-agnostic access to package adoption data. The collection
/// service depends on this interface only; the per-provider adapters
/// and the in-memory fake both implement it. Per provider, exactly one
/// implementation is registered (the design forbids multiple registrations).
/// </summary>
public interface IPackageMetricsProvider
{
    /// <summary>Stable version string for the response shape; stored on every observation.</summary>
    string ProviderVersion { get; }

    /// <summary>The provider this adapter serves (one adapter per provider).</summary>
    PackageProvider Provider { get; }

    /// <summary>Lightweight existence probe; should be cheap and never paginated.</summary>
    Task<PackageMetadataResult> ProbeAsync(string coordinate, CancellationToken cancellationToken);

    /// <summary>Page through adoption observations for one coordinate. The unit/window filter is applied by the adapter.</summary>
    Task<PackageMetricPage> GetObservationsAsync(
        string coordinate,
        PackageUnit unit,
        PackageWindow window,
        DateTimeOffset sinceUtc,
        string? cursor,
        CancellationToken cancellationToken);
}
