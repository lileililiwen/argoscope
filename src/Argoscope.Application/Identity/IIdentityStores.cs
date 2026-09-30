using Argoscope.Domain.Common;
using Argoscope.Domain.Identity;
using Argoscope.Domain.Portfolios;

namespace Argoscope.Application.Identity;

/// <summary>Persistence contracts for tenants. Infrastructure implements with EF
/// Core; tests use in-memory stores.</summary>
public interface ITenantStore
{
    Task<Tenant?> FindAsync(Id<Tenant> tenantId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Tenant>> ListAllAsync(CancellationToken cancellationToken);
    Task AddAsync(Tenant tenant, CancellationToken cancellationToken);
    Task UpdateAsync(Tenant tenant, CancellationToken cancellationToken);
}

public interface ITenantMembershipStore
{
    Task<TenantMembership?> FindAsync(Id<TenantMembership> membershipId, CancellationToken cancellationToken);
    Task<TenantMembership?> FindByTokenAsync(string inviteToken, CancellationToken cancellationToken);
    Task<TenantMembership?> FindBySubjectAsync(Id<Tenant> tenantId, string subject, CancellationToken cancellationToken);
    Task<IReadOnlyList<TenantMembership>> ListByTenantAsync(Id<Tenant> tenantId, CancellationToken cancellationToken);
    Task AddAsync(TenantMembership membership, CancellationToken cancellationToken);
    Task UpdateAsync(TenantMembership membership, CancellationToken cancellationToken);
}

/// <summary>Portfolio access shaped for the hosted migration. Kept separate
/// from <see cref="Collection.IPortfolioRepository"/> so the migration does
/// not change read-side contracts.</summary>
public interface IPortfolioTenantStore
{
    Task<IReadOnlyList<Portfolio>> ListAllPortfoliosAsync(CancellationToken cancellationToken);
    Task SaveAsync(CancellationToken cancellationToken);
}

/// <summary>Bounded server-side session store (opaque tokens, fixed expiry).
/// Secrets stay in deployment config; only the opaque token travels in the
/// secure HttpOnly cookie.</summary>
public interface ISessionStore
{
    Task<SessionEntry> IssueAsync(Id<Tenant> tenantId, string subject, DateTimeOffset now, CancellationToken cancellationToken);
    Task<SessionEntry?> FindAsync(string token, DateTimeOffset now, CancellationToken cancellationToken);
    Task RevokeAsync(string token, CancellationToken cancellationToken);
}

public sealed record SessionEntry(
    string Token,
    Id<Tenant> TenantId,
    string Subject,
    DateTimeOffset ExpiresAtUtc);
