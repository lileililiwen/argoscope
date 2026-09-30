using Argoscope.Application.Operations;
using Argoscope.Domain.Common;
using Argoscope.Domain.Operations;

namespace Argoscope.UnitTests;

public sealed class InMemoryDeletionStore : IDeletionStore
{
    private readonly Dictionary<Guid, TenantDeletionRequest> _byTenant = new();

    public Task<TenantDeletionRequest?> FindByTenantAsync(Id<Domain.Identity.Tenant> tenantId, CancellationToken ct) =>
        Task.FromResult(_byTenant.TryGetValue(tenantId.Value, out var v) ? v : null);

    public Task AddAsync(TenantDeletionRequest request, CancellationToken ct)
    {
        _byTenant[request.TenantId.Value] = request;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(TenantDeletionRequest request, CancellationToken ct)
    {
        _byTenant[request.TenantId.Value] = request;
        return Task.CompletedTask;
    }
}

public sealed class InMemoryIncidentStore : IIncidentStore
{
    private readonly Dictionary<Guid, OperationalIncident> _byId = new();

    public IReadOnlyList<OperationalIncident> All => _byId.Values.ToList();

    public Task<OperationalIncident?> FindAsync(Id<OperationalIncident> id, CancellationToken ct) =>
        Task.FromResult(_byId.TryGetValue(id.Value, out var v) ? v : null);

    public Task<IReadOnlyList<OperationalIncident>> ListAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<OperationalIncident>>(_byId.Values.OrderByDescending(i => i.OpenedAtUtc).ToList());

    public Task AddAsync(OperationalIncident incident, CancellationToken ct)
    {
        _byId[incident.Id.Value] = incident;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(OperationalIncident incident, CancellationToken ct)
    {
        _byId[incident.Id.Value] = incident;
        return Task.CompletedTask;
    }
}

public sealed class InMemoryRehearsalStore : IRestoreRehearsalStore
{
    private readonly List<RestoreRehearsal> _rows = new();

    public Task<IReadOnlyList<RestoreRehearsal>> ListAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<RestoreRehearsal>>(_rows.OrderByDescending(r => r.StartedAtUtc).ToList());

    public Task AddAsync(RestoreRehearsal rehearsal, CancellationToken ct)
    {
        _rows.Add(rehearsal);
        return Task.CompletedTask;
    }
}
