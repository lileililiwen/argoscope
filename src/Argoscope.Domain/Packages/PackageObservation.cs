using Argoscope.Domain.Common;
using Argoscope.Domain.Snapshots;

namespace Argoscope.Domain.Packages;

/// <summary>
/// One provider-reported observation for a single package association,
/// with a specific (unit, window) tuple. The (PackageAssociationId,
/// Unit, Window, WindowStartUtc, WindowEndUtc) tuple is the
/// idempotency key — a repeated read for the same window upserts in
/// place. Unlike units or windows are never combined: each series is
/// surfaced independently by the read API.
/// </summary>
public sealed class PackageObservation : Entity<Id<PackageObservation>>
{
    public Id<PackageAssociation> PackageAssociationId { get; private set; }

    public PackageProvider Provider { get; private set; }

    public string ProviderVersion { get; private set; }

    public PackageUnit Unit { get; private set; }

    public PackageWindow Window { get; private set; }

    public DateTimeOffset WindowStartUtc { get; private set; }

    public DateTimeOffset WindowEndUtc { get; private set; }

    public double Value { get; private set; }

    public ProviderResultStatus Status { get; private set; }

    public bool IsComplete { get; private set; }

    public DateTimeOffset ObservedAtUtc { get; private set; }

    public DateTimeOffset CollectedAtUtc { get; private set; }

    public string? DiagnosticCode { get; private set; }

    private PackageObservation() : base() { }

    public PackageObservation(
        Id<PackageAssociation> packageAssociationId,
        PackageProvider provider,
        string providerVersion,
        PackageUnit unit,
        PackageWindow window,
        DateTimeOffset windowStartUtc,
        DateTimeOffset windowEndUtc,
        double value,
        ProviderResultStatus status,
        bool isComplete,
        DateTimeOffset observedAtUtc,
        DateTimeOffset collectedAtUtc,
        string? diagnosticCode = null)
        : base(Id<PackageObservation>.New())
    {
        if (windowEndUtc < windowStartUtc)
        {
            throw new DomainException("validation", "Window end must be on or after window start.");
        }
        if (string.IsNullOrWhiteSpace(providerVersion))
        {
            throw new DomainException("validation", "Provider version is required.");
        }
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new DomainException("validation", "Value must be a finite number.");
        }

        PackageAssociationId = packageAssociationId;
        Provider = provider;
        ProviderVersion = providerVersion.Trim();
        Unit = unit;
        Window = window;
        WindowStartUtc = windowStartUtc;
        WindowEndUtc = windowEndUtc;
        Value = value;
        Status = status;
        IsComplete = isComplete;
        ObservedAtUtc = observedAtUtc;
        CollectedAtUtc = collectedAtUtc;
        DiagnosticCode = diagnosticCode;
    }
}
