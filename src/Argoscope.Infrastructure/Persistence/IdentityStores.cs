using Argoscope.Application.Identity;
using Argoscope.Domain.Common;
using Argoscope.Domain.Identity;
using Argoscope.Domain.Portfolios;
using Microsoft.EntityFrameworkCore;

namespace Argoscope.Infrastructure.Persistence;

public sealed class EfTenantStore : ITenantStore
{
    private readonly ArgoscopeDbContext _db;
    public EfTenantStore(ArgoscopeDbContext db) => _db = db;

    public Task<Tenant?> FindAsync(Id<Tenant> tenantId, CancellationToken cancellationToken) =>
        _db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);

    public async Task<IReadOnlyList<Tenant>> ListAllAsync(CancellationToken cancellationToken) =>
        await _db.Tenants.OrderBy(t => t.CreatedAtUtc).ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task AddAsync(Tenant tenant, CancellationToken cancellationToken)
    {
        await _db.Tenants.AddAsync(tenant, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateAsync(Tenant tenant, CancellationToken cancellationToken)
    {
        _db.Tenants.Update(tenant);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

public sealed class EfTenantMembershipStore : ITenantMembershipStore
{
    private readonly ArgoscopeDbContext _db;
    public EfTenantMembershipStore(ArgoscopeDbContext db) => _db = db;

    public Task<TenantMembership?> FindAsync(Id<TenantMembership> membershipId, CancellationToken cancellationToken) =>
        _db.TenantMemberships.FirstOrDefaultAsync(m => m.Id == membershipId, cancellationToken);

    public Task<TenantMembership?> FindByTokenAsync(string inviteToken, CancellationToken cancellationToken) =>
        _db.TenantMemberships.FirstOrDefaultAsync(m => m.InviteToken == inviteToken, cancellationToken);

    public Task<TenantMembership?> FindBySubjectAsync(Id<Tenant> tenantId, string subject, CancellationToken cancellationToken) =>
        _db.TenantMemberships.FirstOrDefaultAsync(
            m => m.TenantId == tenantId && m.Subject == subject, cancellationToken);

    public async Task<IReadOnlyList<TenantMembership>> ListByTenantAsync(Id<Tenant> tenantId, CancellationToken cancellationToken) =>
        await _db.TenantMemberships
            .Where(m => m.TenantId == tenantId)
            .OrderBy(m => m.CreatedAtUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task AddAsync(TenantMembership membership, CancellationToken cancellationToken)
    {
        await _db.TenantMemberships.AddAsync(membership, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateAsync(TenantMembership membership, CancellationToken cancellationToken)
    {
        _db.TenantMemberships.Update(membership);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

public sealed class EfPortfolioTenantStore : IPortfolioTenantStore
{
    private readonly ArgoscopeDbContext _db;
    public EfPortfolioTenantStore(ArgoscopeDbContext db) => _db = db;

    public async Task<IReadOnlyList<Portfolio>> ListAllPortfoliosAsync(CancellationToken cancellationToken) =>
        await _db.Portfolios.OrderBy(p => p.CreatedAtUtc).ToListAsync(cancellationToken).ConfigureAwait(false);

    public Task SaveAsync(CancellationToken cancellationToken) =>
        _db.SaveChangesAsync(cancellationToken);
}
