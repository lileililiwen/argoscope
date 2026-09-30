using Argoscope.Application.Operations;
using Argoscope.Domain.Common;
using Argoscope.Domain.Identity;
using Argoscope.Domain.Operations;
using Microsoft.Extensions.Options;
using Xunit;

namespace Argoscope.UnitTests;

/// <summary>BFS fixtures plus DFS coverage for hosted operations: safe
/// release gating (R1), recoverable backups with measured objectives (R2)
/// and auditable deletion/incidents (R3).</summary>
public sealed class OperationsServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

    private sealed class Fixture
    {
        public InMemoryDeletionStore Deletions { get; } = new();
        public InMemoryIncidentStore Incidents { get; } = new();
        public InMemoryRehearsalStore Rehearsals { get; } = new();
        public OperationsOptions Targets { get; } = new();

        public OperationsService Service() => new(
            Deletions, Incidents, Rehearsals,
            Microsoft.Extensions.Options.Options.Create(Targets),
            new FixedClock(Now));
    }

    [Fact]
    public async Task DeletionLifecycle_TombstoneThenPurgeThenExpiry()
    {
        var fx = new Fixture();
        var svc = fx.Service();
        var tenant = Id<Tenant>.From(Guid.NewGuid());

        var created = await svc.RequestDeletionAsync(tenant, "owner-1", CancellationToken.None);
        Assert.True(created.IsSuccess);
        Assert.Equal("Tombstoned", created.Value.Status);
        Assert.Equal(Now.AddDays(30), created.Value.ActivePurgeDueAtUtc);
        Assert.Equal(Now.AddDays(30), created.Value.BackupExpiryDueAtUtc);

        var duplicate = await svc.RequestDeletionAsync(tenant, "owner-1", CancellationToken.None);
        Assert.False(duplicate.IsSuccess);
        Assert.Equal("conflict", duplicate.Error!.Value.Code);

        // Backup expiry before active purge is rejected.
        var early = await svc.MarkBackupExpiredAsync(tenant, CancellationToken.None);
        Assert.False(early.IsSuccess);
        Assert.Equal("conflict", early.Error!.Value.Code);

        var purged = await svc.MarkActivePurgedAsync(tenant, CancellationToken.None);
        Assert.True(purged.IsSuccess);
        Assert.Equal("ActivePurged", purged.Value.Status);
        Assert.NotNull(purged.Value.ActivePurgedAtUtc);

        var expired = await svc.MarkBackupExpiredAsync(tenant, CancellationToken.None);
        Assert.True(expired.IsSuccess);
        Assert.Equal("BackupExpired", expired.Value.Status);
        Assert.NotNull(expired.Value.BackupExpiredAtUtc);
    }

    [Fact]
    public async Task Deletion_UnknownTenant_ReturnsNotFound()
    {
        var fx = new Fixture();
        var missing = await fx.Service().MarkActivePurgedAsync(
            Id<Tenant>.From(Guid.NewGuid()), CancellationToken.None);
        Assert.False(missing.IsSuccess);
        Assert.Equal("not_found", missing.Error!.Value.Code);
    }

    [Fact]
    public async Task Incident_OpenAndResolve_IsAuditable()
    {
        var fx = new Fixture();
        var svc = fx.Service();

        var badSeverity = await svc.OpenIncidentAsync("x", "Bogus", "scope", "summary", CancellationToken.None);
        Assert.False(badSeverity.IsSuccess);

        var opened = await svc.OpenIncidentAsync(
            "Database failover", "Critical", "postgres", "Primary unreachable.", CancellationToken.None);
        Assert.True(opened.IsSuccess);
        Assert.Equal("Open", opened.Value.Status);
        Assert.Equal("Critical", opened.Value.Severity);

        var resolved = await svc.ResolveIncidentAsync(
            Id<OperationalIncident>.From(opened.Value.Id), "Failed over to standby.", CancellationToken.None);
        Assert.True(resolved.IsSuccess);
        Assert.Equal("Resolved", resolved.Value.Status);
        Assert.NotNull(resolved.Value.ResolvedAtUtc);

        var again = await svc.ResolveIncidentAsync(
            Id<OperationalIncident>.From(opened.Value.Id), "Again.", CancellationToken.None);
        Assert.False(again.IsSuccess);
        Assert.Equal("conflict", again.Error!.Value.Code);
    }

    [Fact]
    public async Task Rehearsal_MeetingObjectives_RecordsNoIncident()
    {
        var fx = new Fixture();
        var result = await fx.Service().RecordRehearsalAsync(
            "rev-123", "db-123", Now.AddHours(-2), Now,
            rpoHoursMeasured: 1, rtoHoursMeasured: 2,
            integrityOk: true, "operator", CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.True(result.Value.MeetsObjectives);
        Assert.Empty(fx.Incidents.All);
    }

    [Fact]
    public async Task Rehearsal_MissingObjectives_OpensRemediationIncident()
    {
        var fx = new Fixture();
        var result = await fx.Service().RecordRehearsalAsync(
            "rev-124", "db-124", Now.AddHours(-10), Now,
            rpoHoursMeasured: 30, rtoHoursMeasured: 2,
            integrityOk: true, "operator", CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.False(result.Value.MeetsObjectives);
        var incident = Assert.Single(fx.Incidents.All);
        Assert.Contains("rev-124", incident.Title);
        Assert.Equal(IncidentStatus.Open, incident.Status);
    }

    [Fact]
    public async Task Rehearsal_FailedIntegrity_OpensRemediationIncident()
    {
        var fx = new Fixture();
        var result = await fx.Service().RecordRehearsalAsync(
            "rev-125", "db-125", Now.AddHours(-1), Now,
            rpoHoursMeasured: 1, rtoHoursMeasured: 1,
            integrityOk: false, "operator", CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.False(result.Value.MeetsObjectives);
        Assert.Single(fx.Incidents.All);
    }

    [Fact]
    public void ReleaseInfo_HasImmutableRevision()
    {
        var release = ReleaseInfoProvider.Current(Now);
        Assert.False(string.IsNullOrWhiteSpace(release.Revision));
        Assert.Contains(release.Revision, release.Artifact);
        Assert.Equal(Now, release.BuiltAtUtc);
    }
}
