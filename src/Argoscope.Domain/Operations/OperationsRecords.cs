using Argoscope.Domain.Common;

namespace Argoscope.Domain.Operations;

/// <summary>Tenant-data deletion lifecycle. A deletion request tombstones
/// immediately (hosted reads stop), removes active database rows within 30
/// days, and expires backup copies within the backup retention window.
/// Every transition is auditable; deletion failure stays visible.</summary>
public enum DeletionStatus
{
    Tombstoned = 0,
    ActivePurged = 1,
    BackupExpired = 2,
}

public sealed class TenantDeletionRequest : Entity<Id<TenantDeletionRequest>>
{
    public const int MaxRequestedByLength = 300;

    public Id<Identity.Tenant> TenantId { get; private set; }

    public string RequestedBy { get; private set; }

    public DateTimeOffset RequestedAtUtc { get; private set; }

    public DateTimeOffset ActivePurgeDueAtUtc { get; private set; }

    public DateTimeOffset BackupExpiryDueAtUtc { get; private set; }

    public DeletionStatus Status { get; private set; }

    public DateTimeOffset? ActivePurgedAtUtc { get; private set; }

    public DateTimeOffset? BackupExpiredAtUtc { get; private set; }

    private TenantDeletionRequest() : base()
    {
        RequestedBy = string.Empty;
    }

    public TenantDeletionRequest(
        Id<Identity.Tenant> tenantId, string requestedBy, DateTimeOffset now,
        TimeSpan activePurgeWindow, TimeSpan backupRetention)
        : base(Id<TenantDeletionRequest>.New())
    {
        if (string.IsNullOrWhiteSpace(requestedBy))
        {
            throw new DomainException("validation", "RequestedBy is required.");
        }

        TenantId = tenantId;
        RequestedBy = requestedBy.Trim();
        RequestedAtUtc = now;
        ActivePurgeDueAtUtc = now.Add(activePurgeWindow);
        BackupExpiryDueAtUtc = now.Add(backupRetention);
        Status = DeletionStatus.Tombstoned;
    }

    public void MarkActivePurged(DateTimeOffset now)
    {
        if (Status != DeletionStatus.Tombstoned)
        {
            throw new DomainException("conflict", "Deletion is not awaiting active purge.");
        }

        Status = DeletionStatus.ActivePurged;
        ActivePurgedAtUtc = now;
    }

    public void MarkBackupExpired(DateTimeOffset now)
    {
        if (Status != DeletionStatus.ActivePurged)
        {
            throw new DomainException("conflict", "Backup expiry requires active purge first.");
        }

        Status = DeletionStatus.BackupExpired;
        BackupExpiredAtUtc = now;
    }
}

/// <summary>Incident severity for the operator runbook.</summary>
public enum IncidentSeverity
{
    Low = 0,
    Medium = 1,
    High = 2,
    Critical = 3,
}

public enum IncidentStatus
{
    Open = 0,
    Acknowledged = 1,
    Resolved = 2,
}

/// <summary>Operator incident evidence. Records the window, scope and
/// remediation without exposing tenant secrets.</summary>
public sealed class OperationalIncident : Entity<Id<OperationalIncident>>
{
    public const int MaxTitleLength = 200;
    public const int MaxSummaryLength = 2000;

    public string Title { get; private set; }

    public IncidentSeverity Severity { get; private set; }

    public IncidentStatus Status { get; private set; }

    public string Scope { get; private set; }

    public string Summary { get; private set; }

    public DateTimeOffset OpenedAtUtc { get; private set; }

    public DateTimeOffset? ResolvedAtUtc { get; private set; }

    public string? Resolution { get; private set; }

    private OperationalIncident() : base()
    {
        Title = string.Empty;
        Scope = string.Empty;
        Summary = string.Empty;
    }

    public OperationalIncident(string title, IncidentSeverity severity, string scope, string summary, DateTimeOffset now)
        : base(Id<OperationalIncident>.New())
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new DomainException("validation", "Incident title is required.");
        }

        Title = title.Trim();
        Severity = severity;
        Scope = string.IsNullOrWhiteSpace(scope) ? "hosted-service" : scope.Trim();
        Summary = summary ?? string.Empty;
        Status = IncidentStatus.Open;
        OpenedAtUtc = now;
    }

    public void Acknowledge()
    {
        if (Status != IncidentStatus.Open)
        {
            throw new DomainException("conflict", "Only open incidents can be acknowledged.");
        }

        Status = IncidentStatus.Acknowledged;
    }

    public void Resolve(string? resolution, DateTimeOffset now)
    {
        if (Status == IncidentStatus.Resolved)
        {
            throw new DomainException("conflict", "Incident is already resolved.");
        }

        Status = IncidentStatus.Resolved;
        Resolution = resolution ?? string.Empty;
        ResolvedAtUtc = now;
    }
}

/// <summary>Isolated restore-rehearsal evidence. Measured recovery objectives
/// are recorded against the artifact and database revision; a failed or
/// over-objective rehearsal blocks hosted launch and opens remediation.</summary>
public sealed class RestoreRehearsal : Entity<Id<RestoreRehearsal>>
{
    public string ArtifactRevision { get; private set; }

    public string DatabaseRevision { get; private set; }

    public DateTimeOffset StartedAtUtc { get; private set; }

    public DateTimeOffset FinishedAtUtc { get; private set; }

    public double RpoHoursMeasured { get; private set; }

    public double RtoHoursMeasured { get; private set; }

    public bool IntegrityOk { get; private set; }

    public bool MeetsObjectives { get; private set; }

    public string RecordedBy { get; private set; }

    private RestoreRehearsal() : base()
    {
        ArtifactRevision = string.Empty;
        DatabaseRevision = string.Empty;
        RecordedBy = string.Empty;
    }

    public RestoreRehearsal(
        string artifactRevision, string databaseRevision,
        DateTimeOffset startedAtUtc, DateTimeOffset finishedAtUtc,
        double rpoHoursMeasured, double rtoHoursMeasured,
        bool integrityOk, bool meetsObjectives, string recordedBy)
        : base(Id<RestoreRehearsal>.New())
    {
        if (string.IsNullOrWhiteSpace(artifactRevision))
        {
            throw new DomainException("validation", "ArtifactRevision is required.");
        }

        if (finishedAtUtc < startedAtUtc)
        {
            throw new DomainException("validation", "FinishedAt must not precede StartedAt.");
        }

        ArtifactRevision = artifactRevision.Trim();
        DatabaseRevision = string.IsNullOrWhiteSpace(databaseRevision) ? artifactRevision.Trim() : databaseRevision.Trim();
        StartedAtUtc = startedAtUtc;
        FinishedAtUtc = finishedAtUtc;
        RpoHoursMeasured = rpoHoursMeasured;
        RtoHoursMeasured = rtoHoursMeasured;
        IntegrityOk = integrityOk;
        MeetsObjectives = meetsObjectives;
        RecordedBy = recordedBy ?? string.Empty;
    }
}
