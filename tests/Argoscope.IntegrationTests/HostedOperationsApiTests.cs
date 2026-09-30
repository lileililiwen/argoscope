using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Argoscope.Domain.Packages;
using Argoscope.Domain.Portfolios;
using Argoscope.GitHub;
using Argoscope.Infrastructure.Persistence;
using Argoscope.Packages;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Argoscope.IntegrationTests;

/// <summary>Isolated Hosted-mode operations factory with its own InMemory
/// database so deletion/incident/rehearsal fixtures never leak.</summary>
public sealed class OperationsApiFactory : WebApplicationFactory<Program>
{
    public Dictionary<string, string?> ConfigOverrides { get; } = new()
    {
        ["Identity:Mode"] = "Hosted",
        ["Identity:OidcIssuer"] = "https://issuer.example",
        ["Identity:OidcAudience"] = "argoscope-test",
    };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");
        builder.ConfigureAppConfiguration(cb => cb.AddInMemoryCollection(ConfigOverrides!));
        builder.ConfigureServices(services =>
        {
            var fakeGitHub = new FakeGitHubRepositoryProvider();
            services.RemoveAll<IGitHubRepositoryProvider>();
            services.AddSingleton(fakeGitHub);
            services.AddSingleton<IGitHubRepositoryProvider>(sp => sp.GetRequiredService<FakeGitHubRepositoryProvider>());

            services.RemoveAll<IPackageMetricsProvider>();
            foreach (PackageProvider provider in Enum.GetValues<PackageProvider>())
            {
                var fake = new FakePackageMetricsProvider(provider,
                    providerVersion: $"fake-{provider.ToString().ToLowerInvariant()}-1");
                services.AddSingleton(fake);
                services.AddSingleton<IPackageMetricsProvider>(fake);
            }

            services.RemoveAll<DbContextOptions<ArgoscopeDbContext>>();
            services.AddDbContext<ArgoscopeDbContext>(opts => opts.UseInMemoryDatabase("argoscope-operations-test"));
        });
    }
}

/// <summary>API-level operations matrix: split health gates without secrets,
/// immutable release identity, owner-only deletion/incident/rehearsal flows
/// with tenant isolation, and remediation on missed objectives.</summary>
public sealed class HostedOperationsApiTests : IDisposable
{
    private readonly OperationsApiFactory _factory;

    public HostedOperationsApiTests()
    {
        _factory = new OperationsApiFactory();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ArgoscopeDbContext>();
        db.Database.EnsureDeleted();
        db.Database.EnsureCreated();
    }

    public void Dispose() => _factory.Dispose();

    private static void Auth(HttpClient client, Guid tenantId, string subject)
    {
        client.DefaultRequestHeaders.Remove("X-Argoscope-Tenant");
        client.DefaultRequestHeaders.Remove("X-Argoscope-User");
        client.DefaultRequestHeaders.Add("X-Argoscope-Tenant", tenantId.ToString());
        client.DefaultRequestHeaders.Add("X-Argoscope-User", subject);
    }

    private async Task<Guid> CreateTenantAsync(string name, string owner)
    {
        using var client = _factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/v1/tenants", new { name, ownerSubject = owner });
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var tenant = await resp.Content.ReadFromJsonAsync<TenantDto>();
        return tenant!.TenantId;
    }

    private async Task<string> InviteAndAcceptAsync(Guid tenantId, string owner, string display, string role, string subject)
    {
        using var o = _factory.CreateClient();
        Auth(o, tenantId, owner);
        var inviteResp = await o.PostAsJsonAsync(
            $"/api/v1/tenants/{tenantId}/invites", new { displayName = display, role });
        Assert.Equal(HttpStatusCode.Created, inviteResp.StatusCode);
        var invite = await inviteResp.Content.ReadFromJsonAsync<InviteCreatedDto>();
        using var anon = _factory.CreateClient();
        var accept = await anon.PostAsJsonAsync(
            "/api/v1/invites/accept", new { token = invite!.InviteToken, subject });
        Assert.Equal(HttpStatusCode.OK, accept.StatusCode);
        return subject;
    }

    [Fact]
    public async Task HealthGates_ExposeStatusOnly_NoSecrets()
    {
        using var client = _factory.CreateClient();

        var live = await client.GetAsync("/api/v1/health/live");
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        var liveBody = await live.Content.ReadAsStringAsync();
        Assert.Contains("revision", liveBody);
        Assert.DoesNotContain("connection", liveBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", liveBody, StringComparison.OrdinalIgnoreCase);

        var ready = await client.GetAsync("/api/v1/health/ready");
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        var readyBody = await ready.Content.ReadAsStringAsync();
        Assert.Contains("ready", readyBody);
        Assert.Contains("database", readyBody);
        Assert.DoesNotContain("connection", readyBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", readyBody, StringComparison.OrdinalIgnoreCase);

        var release = await client.GetAsync("/api/v1/ops/release");
        Assert.Equal(HttpStatusCode.OK, release.StatusCode);
        var releaseBody = await release.Content.ReadAsStringAsync();
        Assert.Contains("revision", releaseBody);
        Assert.Contains("artifact", releaseBody);
        Assert.DoesNotContain("secret", releaseBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeletionLifecycle_OwnerOnly_WithTenantIsolation()
    {
        var t1 = await CreateTenantAsync("Ops One", "owner-1");
        var t2 = await CreateTenantAsync("Ops Two", "owner-2");
        await InviteAndAcceptAsync(t1, "owner-1", "Viewer V", "Viewer", "viewer-1");

        using var owner = _factory.CreateClient();
        Auth(owner, t1, "owner-1");

        var created = await owner.PostAsJsonAsync(
            $"/api/v1/tenants/{t1}/deletion", new { requestedBy = (string?)null });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var deletion = await created.Content.ReadFromJsonAsync<DeletionDto>();
        Assert.Equal("Tombstoned", deletion!.Status);

        var duplicate = await owner.PostAsJsonAsync(
            $"/api/v1/tenants/{t1}/deletion", new { requestedBy = (string?)null });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        // Cross-tenant read discloses nothing.
        using var other = _factory.CreateClient();
        Auth(other, t2, "owner-2");
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/v1/tenants/{t1}/deletion")).StatusCode);

        // Viewer cannot advance deletion.
        using var viewer = _factory.CreateClient();
        Auth(viewer, t1, "viewer-1");
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await viewer.PostAsync($"/api/v1/tenants/{t1}/deletion/active-purge", null)).StatusCode);

        // Anonymous cannot request deletion.
        using var anon = _factory.CreateClient();
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await anon.PostAsJsonAsync($"/api/v1/tenants/{t1}/deletion", new { requestedBy = "x" })).StatusCode);

        // Ordered operator steps: purge then backup expiry.
        var earlyExpiry = await owner.PostAsync($"/api/v1/tenants/{t1}/deletion/backup-expiry", null);
        Assert.Equal(HttpStatusCode.Conflict, earlyExpiry.StatusCode);

        var purged = await owner.PostAsync($"/api/v1/tenants/{t1}/deletion/active-purge", null);
        Assert.Equal(HttpStatusCode.OK, purged.StatusCode);

        var expired = await owner.PostAsync($"/api/v1/tenants/{t1}/deletion/backup-expiry", null);
        Assert.Equal(HttpStatusCode.OK, expired.StatusCode);
        var done = await expired.Content.ReadFromJsonAsync<DeletionDto>();
        Assert.Equal("BackupExpired", done!.Status);
    }

    [Fact]
    public async Task IncidentsAndRehearsals_OwnerOnly_MissedObjectivesOpenRemediation()
    {
        var t1 = await CreateTenantAsync("Ops Three", "owner-3");
        await InviteAndAcceptAsync(t1, "owner-3", "Viewer W", "Viewer", "viewer-3");

        using var owner = _factory.CreateClient();
        Auth(owner, t1, "owner-3");

        var incident = await owner.PostAsJsonAsync("/api/v1/ops/incidents",
            new { title = "Failover drill", severity = "High", scope = "postgres", summary = "Planned." });
        Assert.Equal(HttpStatusCode.Created, incident.StatusCode);

        using var viewer = _factory.CreateClient();
        Auth(viewer, t1, "viewer-3");
        var viewerAttempt = await viewer.PostAsJsonAsync("/api/v1/ops/incidents",
            new { title = "Nope", severity = "Low", scope = "x", summary = "x" });
        Assert.Equal(HttpStatusCode.Forbidden, viewerAttempt.StatusCode);

        using var anon = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/v1/ops/incidents")).StatusCode);

        var now = DateTimeOffset.UtcNow;
        var passing = await owner.PostAsJsonAsync("/api/v1/ops/restore-rehearsals", new
        {
            artifactRevision = "rev-pass",
            databaseRevision = "db-pass",
            startedAtUtc = now.AddHours(-2).ToString("o"),
            finishedAtUtc = now.ToString("o"),
            rpoHoursMeasured = 1.0,
            rtoHoursMeasured = 2.0,
            integrityOk = true,
            recordedBy = "owner-3",
        });
        Assert.Equal(HttpStatusCode.Created, passing.StatusCode);
        var passBody = await passing.Content.ReadFromJsonAsync<RehearsalDto>();
        Assert.True(passBody!.MeetsObjectives);

        var failing = await owner.PostAsJsonAsync("/api/v1/ops/restore-rehearsals", new
        {
            artifactRevision = "rev-fail",
            databaseRevision = "db-fail",
            startedAtUtc = now.AddHours(-30).ToString("o"),
            finishedAtUtc = now.ToString("o"),
            rpoHoursMeasured = 30.0,
            rtoHoursMeasured = 2.0,
            integrityOk = true,
            recordedBy = "owner-3",
        });
        Assert.Equal(HttpStatusCode.Created, failing.StatusCode);
        var failBody = await failing.Content.ReadFromJsonAsync<RehearsalDto>();
        Assert.False(failBody!.MeetsObjectives);

        // The missed rehearsal opened a visible remediation incident.
        var incidents = await owner.GetFromJsonAsync<List<IncidentDto>>("/api/v1/ops/incidents");
        Assert.NotNull(incidents);
        Assert.Contains(incidents, i => i.Title.Contains("rev-fail"));

        // Responses carry no secrets.
        var raw = await (await owner.GetAsync("/api/v1/ops/restore-rehearsals")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("secret", raw, StringComparison.OrdinalIgnoreCase);
        var withJson = JsonDocument.Parse(raw);
        Assert.Equal(JsonValueKind.Array, withJson.RootElement.ValueKind);
    }

    private sealed record TenantDto(Guid TenantId, string Name);
    private sealed record InviteCreatedDto(MemberDto Member, string? InviteToken);
    private sealed record MemberDto(Guid MembershipId, string Subject);
    private sealed record DeletionDto(Guid Id, Guid TenantId, string Status);
    private sealed record IncidentDto(Guid Id, string Title, string Status);
    private sealed record RehearsalDto(Guid Id, bool MeetsObjectives);
}
