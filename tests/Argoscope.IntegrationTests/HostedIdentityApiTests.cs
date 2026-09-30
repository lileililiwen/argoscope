using System.Net;
using System.Net.Http.Json;
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

/// <summary>Isolated Hosted-mode factory: Identity:Mode=Hosted with OIDC
/// config, fake GitHub/package providers and its own InMemory database.
/// Each test class instance resets the store so tenant fixtures never leak
/// across tests or into the shared SingleOwner ApiFactory suite.</summary>
public sealed class HostedApiFactory : WebApplicationFactory<Program>
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
            services.AddDbContext<ArgoscopeDbContext>(opts => opts.UseInMemoryDatabase("argoscope-hosted-test"));
        });
    }
}

public sealed class HostedIdentityApiTests : IDisposable
{
    private readonly HostedApiFactory _factory;

    public HostedIdentityApiTests()
    {
        _factory = new HostedApiFactory();
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

    private async Task<(Guid TenantId, string Cookie)> CreateTenantAsync(string name, string owner)
    {
        using var client = _factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/v1/tenants", new { name, ownerSubject = owner });
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var tenant = await resp.Content.ReadFromJsonAsync<TenantDto>();
        Assert.NotNull(tenant);
        var cookie = Assert.Single(resp.Headers.GetValues("Set-Cookie"));
        Assert.Contains("argoscope_session=", cookie);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        return (tenant!.TenantId, cookie);
    }

    private async Task<Guid> CreatePortfolioAsync(HttpClient client, string name)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/portfolios", new { name });
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var portfolio = await resp.Content.ReadFromJsonAsync<PortfolioDto>();
        return portfolio!.Id;
    }

    [Fact]
    public async Task TwoTenant_IsolationMatrix_OwnVsCrossVsAnonymous()
    {
        var (t1, _) = await CreateTenantAsync("Tenant One", "owner-1");
        var (t2, _) = await CreateTenantAsync("Tenant Two", "owner-2");

        using var c1 = _factory.CreateClient();
        Auth(c1, t1, "owner-1");
        var p1 = await CreatePortfolioAsync(c1, "T1 Portfolio");

        using var c2 = _factory.CreateClient();
        Auth(c2, t2, "owner-2");
        var p2 = await CreatePortfolioAsync(c2, "T2 Portfolio");

        // Own resource: 200 with the member role applied.
        Assert.Equal(HttpStatusCode.OK, (await c1.GetAsync($"/api/v1/portfolios/{p1}")).StatusCode);

        // Cross-tenant: 404 with no disclosure.
        Assert.Equal(HttpStatusCode.NotFound, (await c2.GetAsync($"/api/v1/portfolios/{p1}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c1.GetAsync($"/api/v1/portfolios/{p2}")).StatusCode);

        // Cross-tenant mutation is also 404, never 403 (no existence leak).
        var crossMut = await c2.PostAsJsonAsync($"/api/v1/portfolios/{p1}/repositories", new
        {
            nodeId = "node-x",
            ownerLogin = "octo",
            name = "x",
            visibility = "Public",
            role = "Owned",
            lifecycle = "OpenSource",
        });
        Assert.Equal(HttpStatusCode.NotFound, crossMut.StatusCode);

        // Anonymous: 401.
        using var anon = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync($"/api/v1/portfolios/{p1}")).StatusCode);

        // List is scoped to the caller's tenant.
        var list1 = await (await c1.GetAsync("/api/v1/portfolios")).Content.ReadFromJsonAsync<List<PortfolioDto>>();
        Assert.Single(list1!);
        Assert.Equal(p1, list1![0].Id);
        var list2 = await (await c2.GetAsync("/api/v1/portfolios")).Content.ReadFromJsonAsync<List<PortfolioDto>>();
        Assert.Single(list2!);
        Assert.Equal(p2, list2![0].Id);
    }

    [Fact]
    public async Task Viewer_Mutation_Forbidden_Editor_Allowed_Owner_Manages()
    {
        var (t1, _) = await CreateTenantAsync("Roles Co", "owner-1");
        using var owner = _factory.CreateClient();
        Auth(owner, t1, "owner-1");
        var portfolio = await CreatePortfolioAsync(owner, "Roles Portfolio");

        // Invite a viewer and an editor; accept both.
        var viewerInvite = await (await owner.PostAsJsonAsync($"/api/v1/tenants/{t1}/invites",
            new { displayName = "Viewer V", role = "Viewer" })).Content.ReadFromJsonAsync<InviteCreatedDto>();
        var editorInvite = await (await owner.PostAsJsonAsync($"/api/v1/tenants/{t1}/invites",
            new { displayName = "Editor E", role = "Editor" })).Content.ReadFromJsonAsync<InviteCreatedDto>();

        using var anon = _factory.CreateClient();
        var acceptViewer = await anon.PostAsJsonAsync("/api/v1/invites/accept",
            new { token = viewerInvite!.InviteToken, subject = "viewer-1" });
        Assert.Equal(HttpStatusCode.OK, acceptViewer.StatusCode);
        var acceptEditor = await anon.PostAsJsonAsync("/api/v1/invites/accept",
            new { token = editorInvite!.InviteToken, subject = "editor-1" });
        Assert.Equal(HttpStatusCode.OK, acceptEditor.StatusCode);

        // Replay is denied.
        var replay = await anon.PostAsJsonAsync("/api/v1/invites/accept",
            new { token = viewerInvite.InviteToken, subject = "viewer-1" });
        Assert.Equal(HttpStatusCode.NotFound, replay.StatusCode);

        using var viewer = _factory.CreateClient();
        Auth(viewer, t1, "viewer-1");
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync($"/api/v1/portfolios/{portfolio}")).StatusCode);
        var viewerMut = await viewer.PostAsJsonAsync($"/api/v1/portfolios/{portfolio}/repositories", new
        {
            nodeId = "node-v",
            ownerLogin = "octo",
            name = "v",
            visibility = "Public",
            role = "Owned",
            lifecycle = "OpenSource",
        });
        Assert.Equal(HttpStatusCode.Forbidden, viewerMut.StatusCode);

        using var editor = _factory.CreateClient();
        Auth(editor, t1, "editor-1");
        var editorMut = await editor.PostAsJsonAsync($"/api/v1/portfolios/{portfolio}/repositories", new
        {
            nodeId = "node-e",
            ownerLogin = "octo",
            name = "e",
            visibility = "Public",
            role = "Owned",
            lifecycle = "OpenSource",
        });
        Assert.Equal(HttpStatusCode.Created, editorMut.StatusCode);

        // Editor cannot manage membership (owner-only).
        var editorInviteAttempt = await editor.PostAsJsonAsync($"/api/v1/tenants/{t1}/invites",
            new { displayName = "Sneaky", role = "Viewer" });
        Assert.Equal(HttpStatusCode.Forbidden, editorInviteAttempt.StatusCode);
    }

    [Fact]
    public async Task LastOwner_RevokeAndDowngrade_Conflict()
    {
        var (t1, _) = await CreateTenantAsync("Solo", "owner-1");
        using var owner = _factory.CreateClient();
        Auth(owner, t1, "owner-1");
        var members = await (await owner.GetAsync($"/api/v1/tenants/{t1}/members")).Content.ReadFromJsonAsync<List<MemberDto>>();
        var sole = Assert.Single(members!);

        var revoke = await owner.PostAsync($"/api/v1/tenants/{t1}/members/{sole.MembershipId}/revoke", content: null);
        Assert.Equal(HttpStatusCode.Conflict, revoke.StatusCode);

        var downgrade = await owner.PutAsJsonAsync($"/api/v1/tenants/{t1}/members/{sole.MembershipId}",
            new { role = "Viewer" });
        Assert.Equal(HttpStatusCode.Conflict, downgrade.StatusCode);

        // Membership state is unchanged.
        var after = await (await owner.GetAsync($"/api/v1/tenants/{t1}/members")).Content.ReadFromJsonAsync<List<MemberDto>>();
        Assert.Equal("Active", Assert.Single(after!).State);
    }

    [Fact]
    public async Task CrossTenant_MemberRead_IsNotFound()
    {
        var (t1, _) = await CreateTenantAsync("Alpha", "owner-1");
        var (t2, _) = await CreateTenantAsync("Beta", "owner-2");
        using var c2 = _factory.CreateClient();
        Auth(c2, t2, "owner-2");
        Assert.Equal(HttpStatusCode.NotFound, (await c2.GetAsync($"/api/v1/tenants/{t1}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c2.GetAsync($"/api/v1/tenants/{t1}/members")).StatusCode);
    }

    [Fact]
    public async Task UnscopedPortfolio_BlockedUntilMigration()
    {
        var (t1, _) = await CreateTenantAsync("Migrator", "owner-1");

        // Legacy row seeded without a tenant (pre-hosted data shape).
        Guid legacyId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ArgoscopeDbContext>();
            var legacy = new Portfolio("Legacy", DateTimeOffset.UtcNow);
            db.Portfolios.Add(legacy);
            db.SaveChanges();
            legacyId = legacy.Id.Value;
        }

        using var client = _factory.CreateClient();
        Auth(client, t1, "owner-1");
        Assert.Equal(HttpStatusCode.Conflict, (await client.GetAsync($"/api/v1/portfolios/{legacyId}")).StatusCode);

        var migrate = await client.PostAsync($"/api/v1/tenants/{t1}/migrate", content: null);
        Assert.Equal(HttpStatusCode.OK, migrate.StatusCode);
        var result = await migrate.Content.ReadFromJsonAsync<MigrationDto>();
        Assert.Equal(1, result!.Assigned);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/v1/portfolios/{legacyId}")).StatusCode);
    }

    [Fact]
    public async Task OidcCallback_ValidatesClaims_And_OutageFailsClosed()
    {
        var (t1, ownerCookie) = await CreateTenantAsync("Oidc Co", "owner-1");

        using var client = _factory.CreateClient();
        // Wrong issuer.
        var badIssuer = await client.PostAsJsonAsync("/api/v1/auth/oidc/callback", new
        {
            tenantId = t1.ToString(),
            issuer = "https://evil.example",
            subject = "owner-1",
            audience = "argoscope-test",
            expiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(5).ToString("o"),
        });
        Assert.Equal(HttpStatusCode.Unauthorized, badIssuer.StatusCode);

        // Expired token.
        var expired = await client.PostAsJsonAsync("/api/v1/auth/oidc/callback", new
        {
            tenantId = t1.ToString(),
            issuer = "https://issuer.example",
            subject = "owner-1",
            audience = "argoscope-test",
            expiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1).ToString("o"),
        });
        Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);

        // Unknown subject (no membership without an invite).
        var ghost = await client.PostAsJsonAsync("/api/v1/auth/oidc/callback", new
        {
            tenantId = t1.ToString(),
            issuer = "https://issuer.example",
            subject = "ghost",
            audience = "argoscope-test",
            expiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(5).ToString("o"),
        });
        Assert.Equal(HttpStatusCode.Unauthorized, ghost.StatusCode);

        // Valid sign-in issues a bounded session cookie.
        var ok = await client.PostAsJsonAsync("/api/v1/auth/oidc/callback", new
        {
            tenantId = t1.ToString(),
            issuer = "https://issuer.example",
            subject = "owner-1",
            audience = "argoscope-test",
            expiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(5).ToString("o"),
        });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.NotEmpty(ok.Headers.GetValues("Set-Cookie"));

        // Existing sessions continue to work; new logins fail closed only
        // under outage (covered at the service level; the API honors 401).
        var sessionClient = _factory.CreateClient();
        sessionClient.DefaultRequestHeaders.Add("Cookie", ownerCookie.Split(';')[0].Trim());
        sessionClient.DefaultRequestHeaders.Add("X-Argoscope-Csrf", "1");
        var portfolios = await sessionClient.GetAsync("/api/v1/portfolios");
        Assert.Equal(HttpStatusCode.OK, portfolios.StatusCode);
    }

    [Fact]
    public async Task CookieMutation_RequiresCsrfHeader()
    {
        var (t1, ownerCookie) = await CreateTenantAsync("Csrf Co", "owner-1");
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ArgoscopeDbContext>();
            var seeded = new Portfolio("Csrf Portfolio", DateTimeOffset.UtcNow);
            seeded.AssignTenant(Domain.Common.Id<Domain.Identity.Tenant>.From(t1), DateTimeOffset.UtcNow);
            db.Portfolios.Add(seeded);
            db.SaveChanges();
        }

        var portfolios = await ListViaCookieAsync(ownerCookie, csrf: false);
        Assert.Equal(HttpStatusCode.OK, portfolios);

        // Reads via cookie do not need CSRF; mutations do.
        using var noCsrf = _factory.CreateClient();
        noCsrf.DefaultRequestHeaders.Add("Cookie", ownerCookie.Split(';')[0].Trim());
        var list = await (await noCsrf.GetAsync("/api/v1/portfolios")).Content.ReadFromJsonAsync<List<PortfolioDto>>();
        var portfolioId = Assert.Single(list!).Id;
        var denied = await noCsrf.PostAsJsonAsync($"/api/v1/portfolios/{portfolioId}/repositories", new
        {
            nodeId = "node-c",
            ownerLogin = "octo",
            name = "c",
            visibility = "Public",
            role = "Owned",
            lifecycle = "OpenSource",
        });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        using var withCsrf = _factory.CreateClient();
        withCsrf.DefaultRequestHeaders.Add("Cookie", ownerCookie.Split(';')[0].Trim());
        withCsrf.DefaultRequestHeaders.Add("X-Argoscope-Csrf", "1");
        var allowed = await withCsrf.PostAsJsonAsync($"/api/v1/portfolios/{portfolioId}/repositories", new
        {
            nodeId = "node-c",
            ownerLogin = "octo",
            name = "c",
            visibility = "Public",
            role = "Owned",
            lifecycle = "OpenSource",
        });
        Assert.Equal(HttpStatusCode.Created, allowed.StatusCode);
    }

    private async Task<HttpStatusCode> ListViaCookieAsync(string cookie, bool csrf)
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", cookie.Split(';')[0].Trim());
        if (csrf) client.DefaultRequestHeaders.Add("X-Argoscope-Csrf", "1");
        return (await client.GetAsync("/api/v1/portfolios")).StatusCode;
    }

    [Fact]
    public async Task RepositorySurface_MediatedThroughMembership()
    {
        var (t1, _) = await CreateTenantAsync("Repo One", "owner-1");
        var (t2, _) = await CreateTenantAsync("Repo Two", "owner-2");

        using var c1 = _factory.CreateClient();
        Auth(c1, t1, "owner-1");
        var portfolio = await CreatePortfolioAsync(c1, "Repo Portfolio");
        var added = await c1.PostAsJsonAsync($"/api/v1/portfolios/{portfolio}/repositories", new
        {
            nodeId = "node-shared",
            ownerLogin = "octo",
            name = "shared",
            visibility = "Public",
            role = "Owned",
            lifecycle = "OpenSource",
        });
        var member = await added.Content.ReadFromJsonAsync<MembershipDto>();
        var repoId = member!.RepositoryId;

        // Same-tenant owner reads the repository surface.
        Assert.Equal(HttpStatusCode.OK, (await c1.GetAsync($"/api/v1/repositories/{repoId}/packages")).StatusCode);

        // Foreign tenant without a membership sees not-found.
        using var c2 = _factory.CreateClient();
        Auth(c2, t2, "owner-2");
        Assert.Equal(HttpStatusCode.NotFound, (await c2.GetAsync($"/api/v1/repositories/{repoId}/packages")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c2.GetAsync($"/api/v1/repositories/{repoId}/metrics")).StatusCode);
    }

    private sealed record TenantDto(Guid TenantId, string Name);
    private sealed record MemberDto(Guid MembershipId, string Role, string State);
    private sealed record InviteCreatedDto(MemberDto Member, string? InviteToken);
    private sealed record PortfolioDto(Guid Id, string Name);
    private sealed record MembershipDto(Guid RepositoryId);
    private sealed record MigrationDto(int Assigned, int Total, Guid TenantId);
}
