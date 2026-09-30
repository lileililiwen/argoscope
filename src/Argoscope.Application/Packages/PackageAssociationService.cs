using Argoscope.Application.Collection;
using Argoscope.Domain.Common;
using Argoscope.Domain.Packages;
using Argoscope.Domain.Repositories;

namespace Argoscope.Application.Packages;

/// <summary>
/// Inputs for creating an owner-managed package association.
/// </summary>
public sealed record CreatePackageAssociationCommand(
    Id<Repository> RepositoryId,
    PackageProvider Provider,
    string Coordinate,
    PackageUnit? DefaultUnit,
    PackageWindow? DefaultWindow,
    DateTimeOffset Now);

public sealed record UpdatePackageAssociationCommand(
    Id<PackageAssociation> AssociationId,
    PackageUnit DefaultUnit,
    PackageWindow DefaultWindow,
    DateTimeOffset Now);

public sealed record RemovePackageAssociationCommand(
    Id<PackageAssociation> AssociationId,
    DateTimeOffset Now);

public sealed record PackageAssociationDto(
    Guid AssociationId,
    Guid RepositoryId,
    string Provider,
    string Coordinate,
    string DefaultUnit,
    string DefaultWindow,
    string Status,
    string? AttentionReason,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

/// <summary>
/// Use cases for the package-association aggregate. The application
/// layer re-validates the provider coordinate on every create, even
/// though the domain entity also validates, so the API can return a
/// consistent 400 problem-details payload.
/// </summary>
public sealed class PackageAssociationService
{
    private readonly IPackageAssociationStore _associations;
    private readonly IRepositoryStore _repositories;
    private readonly IClock _clock;

    public PackageAssociationService(
        IPackageAssociationStore associations,
        IRepositoryStore repositories,
        IClock clock)
    {
        _associations = associations;
        _repositories = repositories;
        _clock = clock;
    }

    public async Task<Result<PackageAssociationDto>> CreateAsync(
        CreatePackageAssociationCommand command,
        CancellationToken cancellationToken)
    {
        if (!PackageCoordinateValidator.IsValid(command.Provider, command.Coordinate))
        {
            return Error.Validation(
                $"Coordinate is not a valid {command.Provider} package. Expected: {PackageCoordinateValidator.DescribeExpected(command.Provider)}.",
                target: "coordinate");
        }

        var repo = await _repositories.FindAsync(command.RepositoryId, cancellationToken).ConfigureAwait(false);
        if (repo is null)
        {
            return Error.NotFound("Repository not found.");
        }

        var normalized = PackageCoordinateValidator.Normalize(command.Provider, command.Coordinate);
        var existing = await _associations
            .FindByRepositoryAndCoordinateAsync(command.RepositoryId, command.Provider, normalized, cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null && existing.Status != PackageAssociationStatus.Removed)
        {
            return ToDto(existing);
        }

        var defaultUnit = command.DefaultUnit ?? PackageUnitDefaults.ByProvider[command.Provider];
        var defaultWindow = command.DefaultWindow ?? PackageUnitDefaults.DefaultWindowByProvider[command.Provider];

        var association = new PackageAssociation(
            command.RepositoryId, command.Provider, normalized, defaultUnit, defaultWindow, command.Now);
        await _associations.AddAsync(association, cancellationToken).ConfigureAwait(false);
        return ToDto(association);
    }

    public async Task<Result<PackageAssociationDto>> UpdateAsync(
        UpdatePackageAssociationCommand command,
        CancellationToken cancellationToken)
    {
        var existing = await _associations.FindAsync(command.AssociationId, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            return Error.NotFound("Package association not found.");
        }
        if (existing.Status == PackageAssociationStatus.Removed)
        {
            return Error.Conflict("Package association has been removed.");
        }
        existing.Reconfigure(command.DefaultUnit, command.DefaultWindow, command.Now);
        await _associations.UpdateAsync(existing, cancellationToken).ConfigureAwait(false);
        return ToDto(existing);
    }

    public async Task<Result> RemoveAsync(
        RemovePackageAssociationCommand command,
        CancellationToken cancellationToken)
    {
        var existing = await _associations.FindAsync(command.AssociationId, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            return Error.NotFound("Package association not found.");
        }
        existing.MarkRemoved(command.Now);
        await _associations.UpdateAsync(existing, cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }

    public async Task<IReadOnlyList<PackageAssociationDto>> ListByRepositoryAsync(
        Id<Repository> repositoryId,
        CancellationToken cancellationToken)
    {
        var associations = await _associations.ListByRepositoryAsync(repositoryId, cancellationToken).ConfigureAwait(false);
        return associations.Select(ToDto).ToList();
    }

    private static PackageAssociationDto ToDto(PackageAssociation a) =>
        new(
            a.Id.Value,
            a.RepositoryId.Value,
            a.Provider.ToString(),
            a.Coordinate,
            a.DefaultUnit.ToString(),
            a.DefaultWindow.ToString(),
            a.Status.ToString(),
            a.AttentionReason,
            a.CreatedAtUtc,
            a.UpdatedAtUtc);
}
