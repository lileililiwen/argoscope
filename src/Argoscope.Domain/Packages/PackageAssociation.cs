using Argoscope.Domain.Common;
using Argoscope.Domain.Repositories;

namespace Argoscope.Domain.Packages;

/// <summary>
/// Owner-confirmed association between an Argoscope repository and a
/// package coordinate at a specific provider. The (Provider, Coordinate)
/// pair is unique; a coordinate is validated against the provider's
/// documented shape at construction time, and the application layer
/// re-validates on every create. Renames or deletions at the registry do
/// not delete the association or its observations — instead the status
/// transitions to <see cref="PackageAssociationStatus.AttentionRequired"/>
/// and the diagnostic reason is exposed for the dashboard.
/// </summary>
public sealed class PackageAssociation : Entity<Id<PackageAssociation>>
{
    public Id<Repository> RepositoryId { get; private set; }

    public PackageProvider Provider { get; private set; }

    public string Coordinate { get; private set; }

    public PackageUnit DefaultUnit { get; private set; }

    public PackageWindow DefaultWindow { get; private set; }

    public PackageAssociationStatus Status { get; private set; }

    public string? AttentionReason { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    private PackageAssociation() : base() { }

    public PackageAssociation(
        Id<Repository> repositoryId,
        PackageProvider provider,
        string coordinate,
        PackageUnit defaultUnit,
        PackageWindow defaultWindow,
        DateTimeOffset now)
        : base(Id<PackageAssociation>.New())
    {
        if (!PackageCoordinateValidator.IsValid(provider, coordinate))
        {
            throw new DomainException(
                "validation",
                $"Coordinate is not a valid {provider} package. Expected: {PackageCoordinateValidator.DescribeExpected(provider)}.");
        }

        RepositoryId = repositoryId;
        Provider = provider;
        Coordinate = PackageCoordinateValidator.Normalize(provider, coordinate);
        DefaultUnit = defaultUnit;
        DefaultWindow = defaultWindow;
        Status = PackageAssociationStatus.Linked;
        CreatedAtUtc = now;
        UpdatedAtUtc = now;
    }

    /// <summary>
    /// Mark the association as needing owner attention when the provider
    /// reports the package is missing, renamed or otherwise unavailable.
    /// Existing observations are not affected.
    /// </summary>
    public void MarkAttentionRequired(string reason, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException("validation", "Attention reason is required.");
        }
        Status = PackageAssociationStatus.AttentionRequired;
        AttentionReason = reason.Trim();
        UpdatedAtUtc = now;
    }

    /// <summary>Recover from an attention state when a later collection succeeds.</summary>
    public void MarkLinked(DateTimeOffset now)
    {
        Status = PackageAssociationStatus.Linked;
        AttentionReason = null;
        UpdatedAtUtc = now;
    }

    /// <summary>Soft-remove the association; the row stays so history remains auditable.</summary>
    public void MarkRemoved(DateTimeOffset now)
    {
        Status = PackageAssociationStatus.Removed;
        UpdatedAtUtc = now;
    }

    /// <summary>
    /// Reconfigure the default unit/window. The provider and coordinate
    /// are immutable for the lifetime of the association; if the owner
    /// wants to switch providers, they remove and re-create. The
    /// attention state is preserved across the reconfiguration.
    /// </summary>
    public void Reconfigure(PackageUnit defaultUnit, PackageWindow defaultWindow, DateTimeOffset now)
    {
        DefaultUnit = defaultUnit;
        DefaultWindow = defaultWindow;
        UpdatedAtUtc = now;
    }
}
