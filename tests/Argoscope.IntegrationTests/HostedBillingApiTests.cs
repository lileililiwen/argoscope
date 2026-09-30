using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
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

/// <summary>Isolated Hosted-mode billing factory: Hosted identity plus a
/// configured webhook secret and its own InMemory database, so signed
/// webhook fixtures never leak into other suites.</summary>
public sealed class BillingApiFactory : WebApplicationFactory<Program>
{
    public Dictionary<string, string?> ConfigOverrides { get; } = new()
    {
        ["Identity:Mode"] = "Hosted",
        ["Identity:OidcIssuer"] = "https://issuer.example",
        ["Identity:OidcAudience"] = "argoscope-test",
        ["Billing:WebhookSecret"] = "integration-webhook-secret",
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
            services.AddDbContext<ArgoscopeDbContext>(opts => opts.UseInMemoryDatabase("argoscope-billing-test"));
        });
    }
}

/// <summary>API-level billing matrix: trial provisioning, signed webhook
/// idempotency, cross-tenant isolation, 402 enforcement, owner-only account
/// actions and secret-free responses.</summary>
public sealed class HostedBillingApiTests : IDisposable
{
    private const string Secret = "integration-webhook-secret";

    private readonly BillingApiFactory _factory;

    public HostedBillingApiTests()
    {
        _factory = new BillingApiFactory();
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
        Assert.NotNull(tenant);
        return tenant!.TenantId;
    }

    private static string Sign(string timestamp, string body)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(Secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes($"{timestamp}.{body}"));
        return $"t={timestamp},v1={Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    private static string SubscriptionBody(
        string eventId, string type, DateTimeOffset created, Guid tenantId,
        string plan, string status, DateTimeOffset periodEnd)
    {
        var id = tenantId.ToString("N");
        return JsonSerializer.Serialize(new
        {
            id = eventId,
            type,
            created = created.ToUnixTimeSeconds(),
            data = new
            {
                customer = $"local-{id}",
                subscription = $"local-sub-{id}",
                plan,
                status,
                current_period_end = periodEnd.ToUnixTimeSeconds(),
                cancel_at_period_end = false,
                tenant_id = tenantId.ToString("D"),
            },
        });
    }

    private async Task<HttpResponseMessage> PostWebhookAsync(string body, string? signature)
    {
        using var client = _factory.CreateClient();
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/billing/webhook")
        {
            Content = content,
        };
        if (signature is not null)
        {
            request.Headers.Add("X-Billing-Signature", signature);
        }

        return await client.SendAsync(request);
    }

    [Fact]
    public async Task TenantCreate_ProvisionsTrialBilling()
    {
        var tenantId = await CreateTenantAsync("Billing Co", "owner-1");

        using var client = _factory.CreateClient();
        Auth(client, tenantId, "owner-1");
        var resp = await client.GetAsync($"/api/v1/tenants/{tenantId}/billing");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var status = await resp.Content.ReadFromJsonAsync<BillingStatusDto>();
        Assert.Equal("trial", status!.PlanId);
        Assert.Equal("Trial", status.Status);
        Assert.True(status.Entitlements.CanMutate);
    }

    [Fact]
    public async Task Webhook_ValidThenDuplicate_AppliesOnce()
    {
        var tenantId = await CreateTenantAsync("Webhook Co", "owner-1");
        var now = DateTimeOffset.UtcNow;
        var body = SubscriptionBody("evt-int-1", "customer.subscription.updated", now,
            tenantId, "pro", "active", now.AddDays(30));

        var first = await PostWebhookAsync(body, Sign(now.ToUnixTimeSeconds().ToString(), body));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        using var client = _factory.CreateClient();
        Auth(client, tenantId, "owner-1");
        var status = await (await client.GetAsync($"/api/v1/tenants/{tenantId}/billing"))
            .Content.ReadFromJsonAsync<BillingStatusDto>();
        Assert.Equal("Active", status!.Status);
        var version = status.Version;

        // Duplicate delivery: 2xx with no repeated effect.
        var duplicate = await PostWebhookAsync(body, Sign(now.ToUnixTimeSeconds().ToString(), body));
        Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
        var after = await (await client.GetAsync($"/api/v1/tenants/{tenantId}/billing"))
            .Content.ReadFromJsonAsync<BillingStatusDto>();
        Assert.Equal(version, after!.Version);
    }

    [Fact]
    public async Task Webhook_BadSignature_IsRejectedWithoutEffect()
    {
        var tenantId = await CreateTenantAsync("Sig Co", "owner-1");
        var now = DateTimeOffset.UtcNow;
        var body = SubscriptionBody("evt-int-bad", "customer.subscription.updated", now,
            tenantId, "pro", "suspended", now.AddDays(30));

        var resp = await PostWebhookAsync(body, "t=123,v1=deadbeef");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);

        using var client = _factory.CreateClient();
        Auth(client, tenantId, "owner-1");
        var status = await (await client.GetAsync($"/api/v1/tenants/{tenantId}/billing"))
            .Content.ReadFromJsonAsync<BillingStatusDto>();
        Assert.Equal("Trial", status!.Status);

        var events = await (await client.GetAsync($"/api/v1/tenants/{tenantId}/billing/events"))
            .Content.ReadFromJsonAsync<List<BillingEventDto>>();
        Assert.Empty(events!);
    }

    [Fact]
    public async Task Webhook_CrossTenant_NeverChangesOtherTenant()
    {
        var tenantA = await CreateTenantAsync("Tenant A", "owner-a");
        var tenantB = await CreateTenantAsync("Tenant B", "owner-b");
        var now = DateTimeOffset.UtcNow;
        var body = SubscriptionBody("evt-int-x", "customer.subscription.updated", now,
            tenantB, "pro", "suspended", now.AddDays(30));

        var resp = await PostWebhookAsync(body, Sign(now.ToUnixTimeSeconds().ToString(), body));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        using var client = _factory.CreateClient();
        Auth(client, tenantA, "owner-a");
        var statusA = await (await client.GetAsync($"/api/v1/tenants/{tenantA}/billing"))
            .Content.ReadFromJsonAsync<BillingStatusDto>();
        Assert.Equal("Trial", statusA!.Status);
        Assert.True(statusA.Entitlements.CanMutate);

        Auth(client, tenantB, "owner-b");
        var statusB = await (await client.GetAsync($"/api/v1/tenants/{tenantB}/billing"))
            .Content.ReadFromJsonAsync<BillingStatusDto>();
        Assert.Equal("Suspended", statusB!.Status);
        Assert.False(statusB.Entitlements.CanMutate);
    }

    [Fact]
    public async Task SuspendedTenant_MutationIs402_ReadStill200()
    {
        var tenantId = await CreateTenantAsync("Suspended Co", "owner-1");
        var now = DateTimeOffset.UtcNow;
        var body = SubscriptionBody("evt-int-susp", "customer.subscription.updated", now,
            tenantId, "pro", "suspended", now.AddDays(30));
        Assert.Equal(HttpStatusCode.OK,
            (await PostWebhookAsync(body, Sign(now.ToUnixTimeSeconds().ToString(), body))).StatusCode);

        using var client = _factory.CreateClient();
        Auth(client, tenantId, "owner-1");

        var denied = await client.PostAsJsonAsync("/api/v1/portfolios", new { name = "Blocked" });
        Assert.Equal(HttpStatusCode.PaymentRequired, denied.StatusCode);
        var problem = await denied.Content.ReadAsStringAsync();
        Assert.Contains("payment_required", problem);
        Assert.Contains("upgradeUrl", problem);

        // Reads still work; data is retained, not deleted.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/portfolios")).StatusCode);
    }

    [Fact]
    public async Task TrialPortfolioLimit_IsEnforcedWith402()
    {
        var tenantId = await CreateTenantAsync("Limit Co", "owner-1");
        using var client = _factory.CreateClient();
        Auth(client, tenantId, "owner-1");

        // Trial plan allows 3 portfolios.
        for (var i = 1; i <= 3; i++)
        {
            var created = await client.PostAsJsonAsync("/api/v1/portfolios", new { name = $"P{i}" });
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        }

        var denied = await client.PostAsJsonAsync("/api/v1/portfolios", new { name = "P4" });
        Assert.Equal(HttpStatusCode.PaymentRequired, denied.StatusCode);
        Assert.Contains("billing_limit_exceeded", await denied.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task BillingStatus_ExposesNoSecretsOrCardData()
    {
        var tenantId = await CreateTenantAsync("Privacy Co", "owner-1");
        using var client = _factory.CreateClient();
        Auth(client, tenantId, "owner-1");

        var json = await (await client.GetAsync($"/api/v1/tenants/{tenantId}/billing"))
            .Content.ReadAsStringAsync();
        Assert.DoesNotContain("secret", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("card", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cvc", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CheckoutAndCancel_AreOwnerOnly()
    {
        var tenantId = await CreateTenantAsync("Checkout Co", "owner-1");
        using var owner = _factory.CreateClient();
        Auth(owner, tenantId, "owner-1");

        // Invite a viewer and accept as a second subject.
        var invite = await owner.PostAsJsonAsync(
            $"/api/v1/tenants/{tenantId}/invites", new { displayName = "Viewer V", role = "Viewer" });
        Assert.Equal(HttpStatusCode.Created, invite.StatusCode);
        var created = await invite.Content.ReadFromJsonAsync<InviteCreatedDto>();
        using var anon = _factory.CreateClient();
        var accept = await anon.PostAsJsonAsync(
            "/api/v1/invites/accept", new { token = created!.InviteToken, subject = "viewer-1" });
        Assert.Equal(HttpStatusCode.OK, accept.StatusCode);

        using var viewer = _factory.CreateClient();
        Auth(viewer, tenantId, "viewer-1");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await viewer.PostAsJsonAsync($"/api/v1/tenants/{tenantId}/billing/checkout", new { planId = "pro" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await viewer.GetAsync($"/api/v1/tenants/{tenantId}/billing/events")).StatusCode);

        var checkout = await owner.PostAsJsonAsync(
            $"/api/v1/tenants/{tenantId}/billing/checkout", new { planId = "pro" });
        Assert.Equal(HttpStatusCode.OK, checkout.StatusCode);
        var checkoutDto = await checkout.Content.ReadFromJsonAsync<CheckoutDto>();
        Assert.Contains("billing.example", checkoutDto!.CheckoutUrl);

        var cancel = await owner.PostAsJsonAsync($"/api/v1/tenants/{tenantId}/billing/cancel", new { });
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        var canceled = await cancel.Content.ReadFromJsonAsync<BillingStatusDto>();
        Assert.True(canceled!.CancelAtPeriodEnd);
    }

    private sealed record TenantDto(Guid TenantId, string Name);

    private sealed record MemberDto(Guid MembershipId, string Role, string State);

    private sealed record InviteCreatedDto(MemberDto Member, string? InviteToken);

    private sealed record EntitlementDto(
        string PlanId, string PlanName, bool CanMutate, int MaxPortfolios,
        bool CanUseAdvancedAnalytics, bool CanUseAlerts, string? ReadOnlyReason);

    private sealed record BillingStatusDto(
        Guid TenantId, string PlanId, string PlanName, string Status, string EffectiveAccess,
        DateTimeOffset CurrentPeriodEndUtc, bool CancelAtPeriodEnd, int Version,
        EntitlementDto Entitlements, string UpgradeUrl);

    private sealed record CheckoutDto(Guid TenantId, string ProviderCustomerId, string CheckoutUrl);

    private sealed record BillingEventDto(
        string ProviderEventId, string EventType, string State, string? PlanId,
        string? Status, DateTimeOffset ProviderCreatedAtUtc, DateTimeOffset ReceivedAtUtc, string? Note);
}
