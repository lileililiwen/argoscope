using Argoscope.Domain.Common;
using Argoscope.Domain.Operations;
using Microsoft.Extensions.Options;

namespace Argoscope.Application.Operations;

/// <summary>Operational workflows: tenant-data deletion lifecycle,
/// incident evidence, and isolated restore-rehearsal recording with
/// measured RPO/RTO evaluation against operator targets.</summary>
public sealed class OperationsService
{
    private readonly IDeletionStore _deletions;
    private readonly IIncidentStore _incidents;
    private readonly IRestoreRehearsalStore _rehearsals;
    private readonly OperationsOptions _options;
    private readonly IClock _clock;

    public OperationsService(
        IDeletionStore deletions,
        IIncidentStore incidents,
        IRestoreRehearsalStore rehearsals,
        IOptions<OperationsOptions> options,
        IClock clock)
    {
        _deletions = deletions;
        _incidents = incidents;
        _rehearsals = rehearsals;
        _options = options.Value;
        _clock = clock;
    }

    public async Task<Result<DeletionDto>> RequestDeletionAsync(
        Id<Domain.Identity.Tenant> tenantId, string requestedBy, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(requestedBy))
        {
            return Error.Validation("RequestedBy is required.");
        }

        var existing = await _deletions.FindByTenantAsync(tenantId, ct).ConfigureAwait(false);
        if (existing is not null && existing.Status != DeletionStatus.BackupExpired)
        {
            return Error.Conflict("A deletion request is already in progress for this tenant.");
        }

        var now = _clock.UtcNow;
        var request = new TenantDeletionRequest(
            tenantId, requestedBy.Trim(), now,
            TimeSpan.FromDays(_options.ActivePurgeDays <= 0 ? 30 : _options.ActivePurgeDays),
            TimeSpan.FromDays(_options.BackupRetentionDays <= 0 ? 30 : _options.BackupRetentionDays));
        await _deletions.AddAsync(request, ct).ConfigureAwait(false);
        return OperationsDtos.ToDto(request);
    }

    public async Task<Result<DeletionDto>> MarkActivePurgedAsync(
        Id<Domain.Identity.Tenant> tenantId, CancellationToken ct)
    {
        var existing = await _deletions.FindByTenantAsync(tenantId, ct).ConfigureAwait(false);
        if (existing is null)
        {
            return Error.NotFound("Deletion request not found.");
        }

        try
        {
            existing.MarkActivePurged(_clock.UtcNow);
        }
        catch (DomainException ex)
        {
            return ex.Code == "conflict" ? Error.Conflict(ex.Message) : Error.Validation(ex.Message);
        }

        await _deletions.UpdateAsync(existing, ct).ConfigureAwait(false);
        return OperationsDtos.ToDto(existing);
    }

    public async Task<Result<DeletionDto>> MarkBackupExpiredAsync(
        Id<Domain.Identity.Tenant> tenantId, CancellationToken ct)
    {
        var existing = await _deletions.FindByTenantAsync(tenantId, ct).ConfigureAwait(false);
        if (existing is null)
        {
            return Error.NotFound("Deletion request not found.");
        }

        try
        {
            existing.MarkBackupExpired(_clock.UtcNow);
        }
        catch (DomainException ex)
        {
            return ex.Code == "conflict" ? Error.Conflict(ex.Message) : Error.Validation(ex.Message);
        }

        await _deletions.UpdateAsync(existing, ct).ConfigureAwait(false);
        return OperationsDtos.ToDto(existing);
    }

    public async Task<Result<IncidentDto>> OpenIncidentAsync(
        string title, string severity, string scope, string summary, CancellationToken ct)
    {
        if (!Enum.TryParse<IncidentSeverity>(severity, ignoreCase: true, out var parsed))
        {
            return Error.Validation("Severity must be Low, Medium, High or Critical.");
        }

        OperationalIncident incident;
        try
        {
            incident = new OperationalIncident(title, parsed, scope ?? string.Empty, summary ?? string.Empty, _clock.UtcNow);
        }
        catch (DomainException ex)
        {
            return Error.Validation(ex.Message);
        }

        await _incidents.AddAsync(incident, ct).ConfigureAwait(false);
        return OperationsDtos.ToDto(incident);
    }

    public async Task<Result<IncidentDto>> ResolveIncidentAsync(
        Id<OperationalIncident> id, string? resolution, CancellationToken ct)
    {
        var incident = await _incidents.FindAsync(id, ct).ConfigureAwait(false);
        if (incident is null)
        {
            return Error.NotFound("Incident not found.");
        }

        try
        {
            incident.Resolve(resolution, _clock.UtcNow);
        }
        catch (DomainException ex)
        {
            return Error.Conflict(ex.Message);
        }

        await _incidents.UpdateAsync(incident, ct).ConfigureAwait(false);
        return OperationsDtos.ToDto(incident);
    }

    public async Task<Result<RehearsalDto>> RecordRehearsalAsync(
        string artifactRevision, string databaseRevision,
        DateTimeOffset startedAtUtc, DateTimeOffset finishedAtUtc,
        double rpoHoursMeasured, double rtoHoursMeasured,
        bool integrityOk, string recordedBy, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(artifactRevision))
        {
            return Error.Validation("ArtifactRevision is required.");
        }

        if (finishedAtUtc < startedAtUtc)
        {
            return Error.Validation("FinishedAt must not precede StartedAt.");
        }

        var meets = integrityOk
            && rpoHoursMeasured <= _options.RpoHours
            && rtoHoursMeasured <= _options.RtoHours;

        RestoreRehearsal rehearsal;
        try
        {
            rehearsal = new RestoreRehearsal(
                artifactRevision.Trim(), (databaseRevision ?? string.Empty).Trim(),
                startedAtUtc, finishedAtUtc,
                rpoHoursMeasured, rtoHoursMeasured,
                integrityOk, meets, recordedBy ?? string.Empty);
        }
        catch (DomainException ex)
        {
            return Error.Validation(ex.Message);
        }

        await _rehearsals.AddAsync(rehearsal, ct).ConfigureAwait(false);

        // A failed or over-objective rehearsal blocks launch: open a
        // remediation record so the failure stays visible.
        if (!meets)
        {
            var incident = new OperationalIncident(
                $"Restore rehearsal missed objectives ({artifactRevision.Trim()})",
                IncidentSeverity.High, "backup-restore",
                $"Integrity={integrityOk}, RPO={rpoHoursMeasured}h (target {_options.RpoHours}h), RTO={rtoHoursMeasured}h (target {_options.RtoHours}h).",
                _clock.UtcNow);
            await _incidents.AddAsync(incident, ct).ConfigureAwait(false);
        }

        return OperationsDtos.ToDto(rehearsal);
    }
}
