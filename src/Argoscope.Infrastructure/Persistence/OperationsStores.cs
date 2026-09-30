using Argoscope.Application.Operations;
using Argoscope.Domain.Common;
using Argoscope.Domain.Operations;
using Microsoft.EntityFrameworkCore;

namespace Argoscope.Infrastructure.Persistence;

public sealed class EfDeletionStore : IDeletionStore
{
    private readonly ArgoscopeDbContext _db;
    public EfDeletionStore(ArgoscopeDbContext db) => _db = db;

    public Task<TenantDeletionRequest?> FindByTenantAsync(Id<Domain.Identity.Tenant> tenantId, CancellationToken ct) =>
        _db.DeletionRequests.FirstOrDefaultAsync(r => r.TenantId == tenantId, ct);

    public async Task AddAsync(TenantDeletionRequest request, CancellationToken ct)
    {
        await _db.DeletionRequests.AddAsync(request, ct).ConfigureAwait(false);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task UpdateAsync(TenantDeletionRequest request, CancellationToken ct)
    {
        _db.DeletionRequests.Update(request);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}

public sealed class EfIncidentStore : IIncidentStore
{
    private readonly ArgoscopeDbContext _db;
    public EfIncidentStore(ArgoscopeDbContext db) => _db = db;

    public Task<OperationalIncident?> FindAsync(Id<OperationalIncident> id, CancellationToken ct) =>
        _db.Incidents.FirstOrDefaultAsync(i => i.Id == id, ct);

    public async Task<IReadOnlyList<OperationalIncident>> ListAsync(CancellationToken ct) =>
        await _db.Incidents.OrderByDescending(i => i.OpenedAtUtc).ToListAsync(ct).ConfigureAwait(false);

    public async Task AddAsync(OperationalIncident incident, CancellationToken ct)
    {
        await _db.Incidents.AddAsync(incident, ct).ConfigureAwait(false);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task UpdateAsync(OperationalIncident incident, CancellationToken ct)
    {
        _db.Incidents.Update(incident);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}

public sealed class EfRestoreRehearsalStore : IRestoreRehearsalStore
{
    private readonly ArgoscopeDbContext _db;
    public EfRestoreRehearsalStore(ArgoscopeDbContext db) => _db = db;

    public async Task<IReadOnlyList<RestoreRehearsal>> ListAsync(CancellationToken ct) =>
        await _db.RestoreRehearsals.OrderByDescending(r => r.StartedAtUtc).ToListAsync(ct).ConfigureAwait(false);

    public async Task AddAsync(RestoreRehearsal rehearsal, CancellationToken ct)
    {
        await _db.RestoreRehearsals.AddAsync(rehearsal, ct).ConfigureAwait(false);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
