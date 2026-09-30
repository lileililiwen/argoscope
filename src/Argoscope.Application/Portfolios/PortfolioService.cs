using Argoscope.Domain.Common;
using Argoscope.Domain.Memberships;
using Argoscope.Domain.Portfolios;
using Argoscope.Domain.Repositories;
using Argoscope.Application.Collection;

namespace Argoscope.Application.Portfolios;

/// <summary>Inputs for creating a portfolio.</summary>
public sealed record CreatePortfolioCommand(string Name, DateTimeOffset Now, Domain.Common.Id<Domain.Identity.Tenant>? TenantId = null);

/// <summary>Inputs for adding a repository to a portfolio.</summary>
public sealed record AddRepositoryCommand(
    Id<Portfolio> PortfolioId,
    string NodeId,
    string OwnerLogin,
    string Name,
    RepositoryVisibility Visibility,
    MembershipRole Role,
    string? Category,
    string Lifecycle,
    DateTimeOffset Now);

public sealed record UpdateMembershipCommand(
    Id<Portfolio> PortfolioId,
    Id<Repository> RepositoryId,
    string? Category,
    string Lifecycle,
    DateTimeOffset Now);

public sealed record PortfolioDto(
    Guid Id, string Name, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc, Guid? TenantId = null);

public sealed record MembershipDto(
    Guid MembershipId,
    Guid RepositoryId,
    string NodeId,
    string OwnerLogin,
    string Name,
    string FullName,
    RepositoryVisibility Visibility,
    string Role,
    string? Category,
    string Lifecycle,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

/// <summary>Use cases for portfolio and membership management.</summary>
public sealed class PortfolioService
{
    private readonly IPortfolioRepository _portfolios;
    private readonly IRepositoryStore _repositories;
    private readonly IMembershipStore _memberships;
    private readonly IClock _clock;

    public PortfolioService(
        IPortfolioRepository portfolios,
        IRepositoryStore repositories,
        IMembershipStore memberships,
        IClock clock)
    {
        _portfolios = portfolios;
        _repositories = repositories;
        _memberships = memberships;
        _clock = clock;
    }

    public async Task<Result<PortfolioDto>> CreateAsync(CreatePortfolioCommand command, CancellationToken cancellationToken)
    {
        var portfolio = new Portfolio(command.Name, command.Now);
        if (command.TenantId.HasValue)
        {
            portfolio.AssignTenant(command.TenantId.Value, command.Now);
        }
        // The store is not in the read-side interface; the infrastructure layer
        // will register a write-side store. For now, cast the same interface to
        // a write method (kept implicit to keep this layer pure).
        if (_portfolios is not IWritePortfolioRepository writer)
        {
            return Error.Validation("Write-side portfolio repository is not configured.");
        }
        await writer.AddAsync(portfolio, cancellationToken).ConfigureAwait(false);
        return new PortfolioDto(portfolio.Id.Value, portfolio.Name, portfolio.CreatedAtUtc, portfolio.UpdatedAtUtc, portfolio.TenantId?.Value);
    }

    public async Task<PortfolioDto?> GetAsync(Id<Portfolio> portfolioId, CancellationToken cancellationToken)
    {
        var p = await _portfolios.FindAsync(portfolioId, cancellationToken).ConfigureAwait(false);
        return p is null ? null : new PortfolioDto(p.Id.Value, p.Name, p.CreatedAtUtc, p.UpdatedAtUtc, p.TenantId?.Value);
    }

    public async Task<Result<MembershipDto>> AddRepositoryAsync(AddRepositoryCommand command, CancellationToken cancellationToken)
    {
        if (_portfolios is not IWritePortfolioRepository portfolioWriter)
        {
            return Error.Validation("Write-side portfolio repository is not configured.");
        }

        var portfolio = await _portfolios.FindAsync(command.PortfolioId, cancellationToken).ConfigureAwait(false);
        if (portfolio is null)
        {
            return Error.NotFound("Portfolio not found.");
        }

        if (!LifecycleStage.IsAllowed(command.Lifecycle))
        {
            return Error.Validation($"Lifecycle must be one of: {string.Join(", ", LifecycleStage.Allowed)}.");
        }

        // Upsert repository by node id, then by locator if needed.
        var repo = await _repositories.FindByNodeIdAsync(command.NodeId, cancellationToken).ConfigureAwait(false);
        if (repo is null)
        {
            repo = new Repository(
                command.NodeId, command.OwnerLogin, command.Name, command.Visibility, command.Now);
            await _repositories.AddAsync(repo, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            repo.UpdateLocator(command.OwnerLogin, command.Name, command.Visibility, command.Now, null, null, null);
            await _repositories.UpdateLocatorAsync(repo, cancellationToken).ConfigureAwait(false);
        }

        var existing = await _memberships.FindByRepositoryAsync(command.PortfolioId, repo.Id, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return new MembershipDto(
                existing.Id.Value,
                repo.Id.Value,
                repo.NodeId, repo.OwnerLogin, repo.Name, repo.FullName,
                repo.Visibility,
                existing.Role.ToString(),
                existing.Category,
                existing.Lifecycle,
                existing.CreatedAtUtc,
                existing.UpdatedAtUtc);
        }

        var membership = new PortfolioRepository(
            command.PortfolioId, repo.Id, command.Role, command.Category, command.Lifecycle, command.Now);
        await _memberships.AddAsync(membership, cancellationToken).ConfigureAwait(false);
        return new MembershipDto(
            membership.Id.Value, repo.Id.Value, repo.NodeId, repo.OwnerLogin, repo.Name, repo.FullName,
            repo.Visibility, membership.Role.ToString(), membership.Category, membership.Lifecycle,
            membership.CreatedAtUtc, membership.UpdatedAtUtc);
    }

    public async Task<Result<MembershipDto>> UpdateMembershipAsync(UpdateMembershipCommand command, CancellationToken cancellationToken)
    {
        var portfolio = await _portfolios.FindAsync(command.PortfolioId, cancellationToken).ConfigureAwait(false);
        if (portfolio is null)
        {
            return Error.NotFound("Portfolio not found.");
        }
        if (!LifecycleStage.IsAllowed(command.Lifecycle))
        {
            return Error.Validation($"Lifecycle must be one of: {string.Join(", ", LifecycleStage.Allowed)}.");
        }
        var existing = await _memberships.FindByRepositoryAsync(command.PortfolioId, command.RepositoryId, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            return Error.NotFound("Membership not found.");
        }
        existing.Update(command.Category, command.Lifecycle, command.Now);
        await _memberships.UpdateAsync(existing, cancellationToken).ConfigureAwait(false);
        var repo = await _repositories.FindAsync(existing.RepositoryId, cancellationToken).ConfigureAwait(false);
        return new MembershipDto(
            existing.Id.Value, existing.RepositoryId.Value, repo?.NodeId ?? string.Empty,
            repo?.OwnerLogin ?? string.Empty, repo?.Name ?? string.Empty, repo?.FullName ?? string.Empty,
            repo?.Visibility ?? RepositoryVisibility.Unknown,
            existing.Role.ToString(), existing.Category, existing.Lifecycle,
            existing.CreatedAtUtc, existing.UpdatedAtUtc);
    }

    public async Task<IReadOnlyList<MembershipDto>> ListMembershipsAsync(Id<Portfolio> portfolioId, CancellationToken cancellationToken)
    {
        var memberships = await _memberships.ListByPortfolioAsync(portfolioId, cancellationToken).ConfigureAwait(false);
        var dtos = new List<MembershipDto>(memberships.Count);
        foreach (var m in memberships)
        {
            var repo = await _repositories.FindAsync(m.RepositoryId, cancellationToken).ConfigureAwait(false);
            if (repo is null)
            {
                continue;
            }
            dtos.Add(new MembershipDto(
                m.Id.Value, repo.Id.Value, repo.NodeId, repo.OwnerLogin, repo.Name, repo.FullName,
                repo.Visibility, m.Role.ToString(), m.Category, m.Lifecycle,
                m.CreatedAtUtc, m.UpdatedAtUtc));
        }
        return dtos;
    }

    public async Task<Result> RemoveMembershipAsync(Id<Portfolio> portfolioId, Id<Repository> repositoryId, CancellationToken cancellationToken)
    {
        var existing = await _memberships.FindByRepositoryAsync(portfolioId, repositoryId, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            return Error.NotFound("Membership not found.");
        }
        await _memberships.RemoveAsync(existing.Id, cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}

/// <summary>Write-side extension of <see cref="IPortfolioRepository"/>; infrastructure implements it.</summary>
public interface IWritePortfolioRepository : IPortfolioRepository
{
    Task AddAsync(Portfolio portfolio, CancellationToken cancellationToken);
}
