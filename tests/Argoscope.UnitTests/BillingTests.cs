using System.Globalization;
using System.Text.Json;
using Argoscope.Application.Billing;
using Argoscope.Application.Identity;
using Argoscope.Domain.Billing;
using Argoscope.Domain.Common;
using Argoscope.Domain.Identity;
using Microsoft.Extensions.Options;
using Xunit;

namespace Argoscope.UnitTests;

/// <summary>BFS fixtures (fake signed provider deliveries incl. duplicate,
/// replay, out-of-order, unknown and transient DB failures) and DFS coverage
/// for hosted billing: authentic/idempotent events (R1), tenant-scoped
/// entitlements with cache invalidation (R2) and provider-hosted checkout
/// without card data (R3).</summary>
public sealed class BillingTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);
    private const string Secret = "test-webhook-secret";
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    private sealed class Fixture
    {
        public InMemoryBillingStore Store { get; } = new();
        public BillingOptions Billing { get; } = new() { WebhookSecret = Secret };
        public IdentityOptions Identity { get; } = new() { Mode = "Hosted" };

        public BillingService Service() => new(
            Store,
            new InMemoryEntitlementCache(),
            Options.Create(Billing),
            Options.Create(Identity));
    }

    private static string Sign(string secret, string timestamp, string body, bool withTimestamp = true)
    {
        var signature = WebhookSigner.ComputeSignature(secret, timestamp, body);
        return withTimestamp ? $"t={timestamp},v1={signature}" : signature;
    }

    private static string SubEventBody(
        string eventId, string type, DateTimeOffset created,
        string customer, string subscription, string plan, string status,
        DateTimeOffset periodEnd, bool cancelAtPeriodEnd = false, string? tenantId = null)
    {
        var data = new Dictionary<string, object?>
        {
            ["customer"] = customer,
            ["subscription"] = subscription,
            ["plan"] = plan,
            ["status"] = status,
            ["current_period_end"] = periodEnd.ToUnixTimeSeconds(),
            ["cancel_at_period_end"] = cancelAtPeriodEnd,
        };
        if (tenantId is not null) data["tenant_id"] = tenantId;
        return JsonSerializer.Serialize(new
        {
            id = eventId,
            type,
            created = created.ToUnixTimeSeconds(),
            data,
        });
    }

    private static async Task<Id<Tenant>> SeedTenantAsync(Fixture f, string plan = "pro")
    {
        var tenantId = Id<Tenant>.From(Guid.NewGuid());
        var customer = new BillingCustomer(tenantId, $"cus-{tenantId.Value:N}", Now);
        await f.Store.AddCustomerAsync(customer, CancellationToken.None);
        var subscription = new BillingSubscription(
            tenantId, $"sub-{tenantId.Value:N}", plan, SubscriptionStatus.Active,
            Now.AddDays(30), Now);
        await f.Store.AddSubscriptionAsync(subscription, CancellationToken.None);
        await f.Store.SaveAsync(CancellationToken.None);
        return tenantId;
    }

    [Fact]
    public async Task ValidEvent_DeliveredTwice_AppliesOnce()
    {
        var f = new Fixture();
        var tenantId = await SeedTenantAsync(f);
        var svc = f.Service();

        var body = SubEventBody("evt-1", "customer.subscription.updated", Now,
            $"cus-{tenantId.Value:N}", $"sub-{tenantId.Value:N}", "pro", "active", Now.AddDays(30));
        var header = Sign(Secret, Now.ToUnixTimeSeconds().ToString(Culture), body);

        var first = await svc.ProcessWebhookAsync(body, header, Now, CancellationToken.None);
        Assert.True(first.IsSuccess);
        Assert.True(first.Value.Applied);
        Assert.False(first.Value.Duplicate);

        var second = await svc.ProcessWebhookAsync(body, header, Now, CancellationToken.None);
        Assert.True(second.IsSuccess);
        Assert.True(second.Value.Duplicate);

        var status = await svc.GetStatusAsync(tenantId, Now, CancellationToken.None);
        Assert.Equal(2, status!.Version);
        Assert.Equal("Active", status.Status);
        var events = await f.Store.ListEventsByTenantAsync(tenantId, CancellationToken.None);
        Assert.Single(events);
    }

    [Fact]
    public async Task InvalidSignature_RejectsWithoutStateChange()
    {
        var f = new Fixture();
        var tenantId = await SeedTenantAsync(f);
        var svc = f.Service();

        var body = SubEventBody("evt-bad", "customer.subscription.updated", Now,
            $"cus-{tenantId.Value:N}", $"sub-{tenantId.Value:N}", "pro", "suspended", Now.AddDays(30));
        var header = Sign("wrong-secret", Now.ToUnixTimeSeconds().ToString(Culture), body);

        var result = await svc.ProcessWebhookAsync(body, header, Now, CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal("validation", result.Error!.Value.Code);

        var status = await svc.GetStatusAsync(tenantId, Now, CancellationToken.None);
        Assert.Equal("Active", status!.Status);
        Assert.Equal(1, status.Version);
        Assert.Null(await f.Store.FindEventAsync("evt-bad", CancellationToken.None));
    }

    [Fact]
    public async Task StaleTimestamp_ReplaysAreRejected()
    {
        var f = new Fixture();
        var tenantId = await SeedTenantAsync(f);
        var svc = f.Service();

        // A captured delivery replayed one hour later falls outside the
        // ±5-minute tolerance window.
        var created = Now.AddHours(-1);
        var body = SubEventBody("evt-old", "customer.subscription.updated", created,
            $"cus-{tenantId.Value:N}", $"sub-{tenantId.Value:N}", "pro", "suspended", Now.AddDays(30));
        var header = Sign(Secret, created.ToUnixTimeSeconds().ToString(Culture), body);

        var result = await svc.ProcessWebhookAsync(body, header, Now, CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal("validation", result.Error!.Value.Code);
        Assert.Null(await f.Store.FindEventAsync("evt-old", CancellationToken.None));
    }

    [Fact]
    public async Task TamperedReplay_SameIdDifferentPayload_IsNoop()
    {
        var f = new Fixture();
        var tenantId = await SeedTenantAsync(f);
        var svc = f.Service();

        var body = SubEventBody("evt-9", "customer.subscription.updated", Now,
            $"cus-{tenantId.Value:N}", $"sub-{tenantId.Value:N}", "pro", "active", Now.AddDays(30));
        var first = await svc.ProcessWebhookAsync(
            body, Sign(Secret, Now.ToUnixTimeSeconds().ToString(Culture), body), Now, CancellationToken.None);
        Assert.True(first.Value.Applied);

        // Attacker replays the id with a downgraded payload and a fresh
        // signature: idempotency keys on the event id, so nothing changes.
        var tampered = SubEventBody("evt-9", "customer.subscription.updated", Now,
            $"cus-{tenantId.Value:N}", $"sub-{tenantId.Value:N}", "trial", "suspended", Now.AddDays(30));
        var replay = await svc.ProcessWebhookAsync(
            tampered, Sign(Secret, Now.ToUnixTimeSeconds().ToString(Culture), tampered), Now, CancellationToken.None);
        Assert.True(replay.IsSuccess);
        Assert.True(replay.Value.Duplicate);

        var status = await svc.GetStatusAsync(tenantId, Now, CancellationToken.None);
        Assert.Equal("pro", status!.PlanId);
        Assert.Equal("Active", status.Status);
    }

    [Fact]
    public async Task OutOfOrder_OlderEvent_IsRecordedButIgnored()
    {
        var f = new Fixture();
        var tenantId = await SeedTenantAsync(f);
        var svc = f.Service();

        var newer = SubEventBody("evt-new", "customer.subscription.updated", Now,
            $"cus-{tenantId.Value:N}", $"sub-{tenantId.Value:N}", "pro", "active", Now.AddDays(60));
        Assert.True((await svc.ProcessWebhookAsync(
            newer, Sign(Secret, Now.ToUnixTimeSeconds().ToString(Culture), newer), Now, CancellationToken.None)).Value.Applied);

        // Older provider timestamp arrives late: recorded as Ignored, newer
        // state (60-day period) is preserved.
        var olderCreated = Now.AddMinutes(-2);
        var older = SubEventBody("evt-old2", "customer.subscription.updated", olderCreated,
            $"cus-{tenantId.Value:N}", $"sub-{tenantId.Value:N}", "starter", "past_due", Now.AddDays(1));
        var outcome = await svc.ProcessWebhookAsync(
            older, Sign(Secret, Now.ToUnixTimeSeconds().ToString(Culture), older), Now, CancellationToken.None);
        Assert.True(outcome.IsSuccess);
        Assert.False(outcome.Value.Applied);
        Assert.Equal("Ignored", outcome.Value.State);

        var status = await svc.GetStatusAsync(tenantId, Now, CancellationToken.None);
        Assert.Equal("pro", status!.PlanId);
        Assert.Equal(Now.AddDays(60), status.CurrentPeriodEndUtc);
    }

    [Fact]
    public async Task UnknownEventType_IsDurablyIgnored()
    {
        var f = new Fixture();
        var tenantId = await SeedTenantAsync(f);
        var svc = f.Service();

        var body = SubEventBody("evt-mystery", "customer.whatever.happened", Now,
            $"cus-{tenantId.Value:N}", $"sub-{tenantId.Value:N}", "pro", "active", Now.AddDays(30));
        var outcome = await svc.ProcessWebhookAsync(
            body, Sign(Secret, Now.ToUnixTimeSeconds().ToString(Culture), body), Now, CancellationToken.None);
        Assert.True(outcome.IsSuccess);
        Assert.False(outcome.Value.Applied);

        var stored = await f.Store.FindEventAsync("evt-mystery", CancellationToken.None);
        Assert.NotNull(stored);
        Assert.Equal(ProviderEventState.Ignored, stored!.State);
        var status = await svc.GetStatusAsync(tenantId, Now, CancellationToken.None);
        Assert.Equal(1, status!.Version);
    }

    [Fact]
    public async Task UnknownProviderIds_TouchNoTenant()
    {
        var f = new Fixture();
        var tenantId = await SeedTenantAsync(f);
        var svc = f.Service();

        var body = SubEventBody("evt-ghost", "customer.subscription.updated", Now,
            "cus-ghost", "sub-ghost", "pro", "active", Now.AddDays(30));
        var outcome = await svc.ProcessWebhookAsync(
            body, Sign(Secret, Now.ToUnixTimeSeconds().ToString(Culture), body), Now, CancellationToken.None);
        Assert.True(outcome.IsSuccess);
        Assert.False(outcome.Value.Applied);

        var status = await svc.GetStatusAsync(tenantId, Now, CancellationToken.None);
        Assert.Equal("Active", status!.Status);
        Assert.Equal(1, status.Version);
        var stored = await f.Store.FindEventAsync("evt-ghost", CancellationToken.None);
        Assert.NotNull(stored);
        Assert.Null(stored!.TenantId);
    }

    [Fact]
    public async Task CrossTenant_EventNeverChangesOtherTenant()
    {
        var f = new Fixture();
        var tenantA = await SeedTenantAsync(f);
        var tenantB = await SeedTenantAsync(f);
        var svc = f.Service();

        var body = SubEventBody("evt-b", "customer.subscription.updated", Now,
            $"cus-{tenantB.Value:N}", $"sub-{tenantB.Value:N}", "starter", "suspended", Now.AddDays(30));
        var outcome = await svc.ProcessWebhookAsync(
            body, Sign(Secret, Now.ToUnixTimeSeconds().ToString(Culture), body), Now, CancellationToken.None);
        Assert.True(outcome.Value.Applied);

        var statusA = await svc.GetStatusAsync(tenantA, Now, CancellationToken.None);
        Assert.Equal("pro", statusA!.PlanId);
        Assert.Equal("Active", statusA.Status);
        Assert.Equal(1, statusA.Version);

        var statusB = await svc.GetStatusAsync(tenantB, Now, CancellationToken.None);
        Assert.Equal("Suspended", statusB!.Status);
        Assert.False(statusB.Entitlements.CanMutate);
    }

    [Fact]
    public async Task EntitlementTransitions_FollowGraceAndPeriodRules()
    {
        var f = new Fixture();
        var svc = f.Service();
        var tenantId = Id<Tenant>.From(Guid.NewGuid());

        // Explicit trial provisioning (no hidden default: missing trial plan
        // provisions nothing).
        var trial = await svc.EnsureTrialAsync(tenantId, Now, CancellationToken.None);
        Assert.NotNull(trial);
        Assert.Equal("Trial", trial!.Status);
        Assert.True(trial.Entitlements.CanMutate);
        Assert.Equal(3, trial.Entitlements.MaxPortfolios);

        // Activate via webhook, then go past-due: grace keeps full access.
        var activate = SubEventBody("evt-act", "customer.subscription.created", Now,
            $"local-{tenantId.Value:N}", $"local-sub-{tenantId.Value:N}", "pro", "active", Now.AddDays(30),
            tenantId: tenantId.Value.ToString("D"));
        Assert.True((await svc.ProcessWebhookAsync(
            activate, Sign(Secret, Now.ToUnixTimeSeconds().ToString(Culture), activate), Now, CancellationToken.None)).Value.Applied);
        var active = await svc.GetStatusAsync(tenantId, Now, CancellationToken.None);
        Assert.Equal("Active", active!.Status);
        Assert.Equal(50, active.Entitlements.MaxPortfolios);

        var pastDueAt = Now.AddDays(1);
        var pastDue = SubEventBody("evt-pd", "invoice.payment_failed", pastDueAt,
            $"local-{tenantId.Value:N}", $"local-sub-{tenantId.Value:N}", "pro", "past_due", Now.AddDays(30),
            tenantId: tenantId.Value.ToString("D"));
        Assert.True((await svc.ProcessWebhookAsync(
            pastDue, Sign(Secret, pastDueAt.ToUnixTimeSeconds().ToString(Culture), pastDue), pastDueAt, CancellationToken.None)).Value.Applied);
        var grace = await svc.GetStatusAsync(tenantId, pastDueAt, CancellationToken.None);
        Assert.Equal("PastDue", grace!.Status);
        Assert.True(grace.Entitlements.CanMutate);

        // Grace elapsed (7 days after period end): read-only, data retained.
        var lapsed = await svc.GetStatusAsync(tenantId, Now.AddDays(40), CancellationToken.None);
        Assert.Equal("PastDue", lapsed!.Status);
        Assert.False(lapsed.Entitlements.CanMutate);
        Assert.NotNull(lapsed.Entitlements.ReadOnlyReason);

        // Canceled stays mutable through the paid-through date.
        var cancelAt = Now.AddDays(41);
        var canceled = SubEventBody("evt-cx", "customer.subscription.deleted", cancelAt,
            $"local-{tenantId.Value:N}", $"local-sub-{tenantId.Value:N}", "pro", "canceled", Now.AddDays(60),
            tenantId: tenantId.Value.ToString("D"));
        Assert.True((await svc.ProcessWebhookAsync(
            canceled, Sign(Secret, cancelAt.ToUnixTimeSeconds().ToString(Culture), canceled), cancelAt, CancellationToken.None)).Value.Applied);
        var throughPeriod = await svc.GetStatusAsync(tenantId, cancelAt, CancellationToken.None);
        Assert.Equal("Canceled", throughPeriod!.Status);
        Assert.True(throughPeriod.Entitlements.CanMutate);
        var afterPeriod = await svc.GetStatusAsync(tenantId, Now.AddDays(61), CancellationToken.None);
        Assert.False(afterPeriod!.Entitlements.CanMutate);
    }

    [Fact]
    public async Task Suspended_IsReadOnly_AndCacheInvalidatesOnChange()
    {
        var f = new Fixture();
        var tenantId = await SeedTenantAsync(f);
        var cache = new InMemoryEntitlementCache();
        var svc = new BillingService(
            f.Store, cache, Options.Create(f.Billing), Options.Create(f.Identity));

        var before = await svc.GetEntitlementsAsync(tenantId, Now, CancellationToken.None);
        Assert.True(before.Entitlements!.CanMutate);
        Assert.True(cache.TryGet(tenantId.Value, before.Version!.Value, out _));

        var body = SubEventBody("evt-susp", "customer.subscription.suspended", Now,
            $"cus-{tenantId.Value:N}", $"sub-{tenantId.Value:N}", "pro", "suspended", Now.AddDays(30));
        await svc.ProcessWebhookAsync(
            body, Sign(Secret, Now.ToUnixTimeSeconds().ToString(Culture), body), Now, CancellationToken.None);

        // The version bump invalidated the snapshot: the new version is not
        // cached until the next read recomputes it.
        var current = await f.Store.FindSubscriptionByTenantAsync(tenantId, CancellationToken.None);
        Assert.NotNull(current);
        Assert.NotEqual(before.Version, current!.Version);
        Assert.False(cache.TryGet(tenantId.Value, current.Version, out _));

        var after = await svc.GetEntitlementsAsync(tenantId, Now, CancellationToken.None);
        Assert.Equal(current.Version, after.Version);
        Assert.False(after.Entitlements!.CanMutate);
        Assert.Equal("Account is suspended.", after.Entitlements.ReadOnlyReason);
        Assert.True(cache.TryGet(tenantId.Value, current.Version, out _));
    }

    [Fact]
    public async Task Checkout_StoresReferencesOnly_AndRequiresOwner()
    {
        var f = new Fixture();
        var svc = f.Service();
        var tenantId = Id<Tenant>.From(Guid.NewGuid());

        var viewer = await svc.StartCheckoutAsync(
            tenantId, "viewer-1", TenantRole.Viewer, "pro", Now, CancellationToken.None);
        Assert.False(viewer.IsSuccess);
        Assert.Equal("forbidden", viewer.Error!.Value.Code);

        var checkout = await svc.StartCheckoutAsync(
            tenantId, "owner-1", TenantRole.Owner, "pro", Now, CancellationToken.None);
        Assert.True(checkout.IsSuccess);
        Assert.StartsWith("local-", checkout.Value.ProviderCustomerId);
        Assert.Contains("billing.example", checkout.Value.CheckoutUrl);

        // No card data anywhere: the customer entity carries provider refs
        // only, and the checkout payload has no credential fields.
        var customer = await f.Store.FindCustomerByTenantAsync(tenantId, CancellationToken.None);
        Assert.NotNull(customer);
        var propertyNames = string.Join(",", customer!.GetType().GetProperties().Select(p => p.Name));
        Assert.DoesNotContain("card", propertyNames, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cvc", propertyNames, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", checkout.Value.CheckoutUrl, StringComparison.OrdinalIgnoreCase);

        var badPlan = await svc.StartCheckoutAsync(
            tenantId, "owner-1", TenantRole.Owner, "nope", Now, CancellationToken.None);
        Assert.False(badPlan.IsSuccess);
    }

    [Fact]
    public async Task TransientDbFailure_LeavesNoEventBehind()
    {
        var f = new Fixture();
        var tenantId = await SeedTenantAsync(f);
        var svc = f.Service();

        f.Store.FailOnSave = true;
        var body = SubEventBody("evt-flaky", "customer.subscription.updated", Now,
            $"cus-{tenantId.Value:N}", $"sub-{tenantId.Value:N}", "pro", "active", Now.AddDays(30));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.ProcessWebhookAsync(
                body, Sign(Secret, Now.ToUnixTimeSeconds().ToString(Culture), body), Now, CancellationToken.None));

        // 5xx before durable record: the provider retry sees a clean slate.
        f.Store.FailOnSave = false;
        Assert.Null(await f.Store.FindEventAsync("evt-flaky", CancellationToken.None));
        var retry = await svc.ProcessWebhookAsync(
            body, Sign(Secret, Now.ToUnixTimeSeconds().ToString(Culture), body), Now, CancellationToken.None);
        Assert.True(retry.IsSuccess);
        Assert.True(retry.Value.Applied);
    }

    [Fact]
    public async Task MissingSecret_FailsClosed()
    {
        var f = new Fixture();
        f.Billing.WebhookSecret = string.Empty;
        var tenantId = await SeedTenantAsync(f);

        var body = SubEventBody("evt-closed", "customer.subscription.updated", Now,
            $"cus-{tenantId.Value:N}", $"sub-{tenantId.Value:N}", "pro", "active", Now.AddDays(30));
        var result = await f.Service().ProcessWebhookAsync(
            body, Sign(Secret, Now.ToUnixTimeSeconds().ToString(Culture), body), Now, CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal("unavailable", result.Error!.Value.Code);
        Assert.Null(await f.Store.FindEventAsync("evt-closed", CancellationToken.None));
    }

    [Fact]
    public async Task Reconcile_ReportsWithoutOverwriting()
    {
        var f = new Fixture();
        var tenantId = await SeedTenantAsync(f, plan: "ghost-plan");
        var svc = f.Service();

        var report = await svc.ReconcileAsync(Now, CancellationToken.None);
        Assert.Equal(1, report.Checked);
        Assert.True(report.Attention >= 1);
        Assert.Contains(report.Findings, finding => finding.Contains("ghost-plan"));

        // Reconciliation never mutates tenant data.
        var status = await svc.GetStatusAsync(tenantId, Now, CancellationToken.None);
        Assert.Equal("ghost-plan", status!.PlanId);
        Assert.False(status.Entitlements.CanMutate);
    }

    /// <summary>Emulates EF transactional persistence: adds are staged and
    /// committed by SaveAsync; a save failure discards the staged batch so a
    /// provider retry sees a clean slate (mirrors a fresh DbContext scope in
    /// production after a 5xx).</summary>
    private sealed class InMemoryBillingStore : IBillingStore
    {
        private readonly Dictionary<Id<Tenant>, BillingCustomer> _customers = new();
        private readonly Dictionary<Id<Tenant>, BillingSubscription> _subscriptions = new();
        private readonly Dictionary<string, ProviderEvent> _events = new();
        private readonly List<BillingCustomer> _pendingCustomers = new();
        private readonly List<BillingSubscription> _pendingSubscriptions = new();
        private readonly List<ProviderEvent> _pendingEvents = new();

        public bool FailOnSave { get; set; }

        public Task<BillingCustomer?> FindCustomerByTenantAsync(Id<Tenant> tenantId, CancellationToken ct) =>
            Task.FromResult(_customers.TryGetValue(tenantId, out var v) ? v : null);

        public Task<BillingCustomer?> FindCustomerByProviderIdAsync(string providerCustomerId, CancellationToken ct) =>
            Task.FromResult(_customers.Values.FirstOrDefault(c => c.ProviderCustomerId == providerCustomerId));

        public Task AddCustomerAsync(BillingCustomer customer, CancellationToken ct)
        {
            _pendingCustomers.Add(customer);
            return Task.CompletedTask;
        }

        public Task<BillingSubscription?> FindSubscriptionByTenantAsync(Id<Tenant> tenantId, CancellationToken ct) =>
            Task.FromResult(_subscriptions.TryGetValue(tenantId, out var v) ? v : null);

        public Task<BillingSubscription?> FindSubscriptionByProviderIdAsync(string providerSubscriptionId, CancellationToken ct) =>
            Task.FromResult(_subscriptions.Values.FirstOrDefault(s => s.ProviderSubscriptionId == providerSubscriptionId));

        public Task AddSubscriptionAsync(BillingSubscription subscription, CancellationToken ct)
        {
            _pendingSubscriptions.Add(subscription);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<BillingSubscription>> ListSubscriptionsAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<BillingSubscription>>(_subscriptions.Values.ToList());

        public Task<ProviderEvent?> FindEventAsync(string providerEventId, CancellationToken ct) =>
            Task.FromResult(_events.TryGetValue(providerEventId, out var v) ? v : null);

        public Task AddEventAsync(ProviderEvent @event, CancellationToken ct)
        {
            _pendingEvents.Add(@event);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ProviderEvent>> ListEventsByTenantAsync(Id<Tenant> tenantId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ProviderEvent>>(
                _events.Values.Where(e => e.TenantId == tenantId).OrderBy(e => e.ReceivedAtUtc).ToList());

        public Task SaveAsync(CancellationToken ct)
        {
            if (FailOnSave)
            {
                _pendingCustomers.Clear();
                _pendingSubscriptions.Clear();
                _pendingEvents.Clear();
                throw new InvalidOperationException("simulated persistence failure");
            }

            foreach (var customer in _pendingCustomers) _customers[customer.TenantId] = customer;
            foreach (var subscription in _pendingSubscriptions) _subscriptions[subscription.TenantId] = subscription;
            foreach (var @event in _pendingEvents) _events[@event.ProviderEventId] = @event;
            _pendingCustomers.Clear();
            _pendingSubscriptions.Clear();
            _pendingEvents.Clear();
            return Task.CompletedTask;
        }
    }
}
