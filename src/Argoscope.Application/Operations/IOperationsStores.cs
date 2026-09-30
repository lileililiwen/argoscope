using Argoscope.Domain.Common;
using Argoscope.Domain.Operations;

namespace Argoscope.Application.Operations;

public sealed record DeletionDto(
    Guid Id, Guid TenantId, string RequestedBy, DateTimeOffset RequestedAtUtc,
    DateTimeOffset ActivePurgeDueAtUtc, DateTimeOffset BackupExpiryDueAtUtc,
    string Status, DateTimeOffset? ActivePurgedAtUtc, DateTimeOffset? BackupExpiredAtUtc);

public sealed record IncidentDto(
    Guid Id, string Title, string Severity, string Status, string Scope,
    string Summary, DateTimeOffset OpenedAtUtc, DateTimeOffset? ResolvedAtUtc, string? Resolution);

public sealed record RehearsalDto(
    Guid Id, string ArtifactRevision, string DatabaseRevision,
    DateTimeOffset StartedAtUtc, DateTimeOffset FinishedAtUtc,
    double RpoHoursMeasured, double RtoHoursMeasured,
    bool IntegrityOk, bool MeetsObjectives, string RecordedBy);

public static class OperationsDtos
{
    public static DeletionDto ToDto(TenantDeletionRequest r) => new(
        r.Id.Value, r.TenantId.Value, r.RequestedBy, r.RequestedAtUtc,
        r.ActivePurgeDueAtUtc, r.BackupExpiryDueAtUtc,
        r.Status.ToString(), r.ActivePurgedAtUtc, r.BackupExpiredAtUtc);

    public static IncidentDto ToDto(OperationalIncident i) => new(
        i.Id.Value, i.Title, i.Severity.ToString(), i.Status.ToString(), i.Scope,
        i.Summary, i.OpenedAtUtc, i.ResolvedAtUtc, i.Resolution);

    public static RehearsalDto ToDto(RestoreRehearsal r) => new(
        r.Id.Value, r.ArtifactRevision, r.DatabaseRevision,
        r.StartedAtUtc, r.FinishedAtUtc,
        r.RpoHoursMeasured, r.RtoHoursMeasured,
        r.IntegrityOk, r.MeetsObjectives, r.RecordedBy);
}

public interface IDeletionStore
{
    Task<TenantDeletionRequest?> FindByTenantAsync(Id<Domain.Identity.Tenant> tenantId, CancellationToken ct);
    Task AddAsync(TenantDeletionRequest request, CancellationToken ct);
    Task UpdateAsync(TenantDeletionRequest request, CancellationToken ct);
}

public interface IIncidentStore
{
    Task<OperationalIncident?> FindAsync(Id<OperationalIncident> id, CancellationToken ct);
    Task<IReadOnlyList<OperationalIncident>> ListAsync(CancellationToken ct);
    Task AddAsync(OperationalIncident incident, CancellationToken ct);
    Task UpdateAsync(OperationalIncident incident, CancellationToken ct);
}

public interface IRestoreRehearsalStore
{
    Task<IReadOnlyList<RestoreRehearsal>> ListAsync(CancellationToken ct);
    Task AddAsync(RestoreRehearsal rehearsal, CancellationToken ct);
}
