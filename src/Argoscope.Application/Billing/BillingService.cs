using System.Text.Json;
using Argoscope.Domain.Billing;
using Argoscope.Domain.Common;
using Argoscope.Domain.Identity;
using Microsoft.Extensions.Options;

namespace Argoscope.Application.Billing;

/// <summary>
/// Subscription lifecycle, provider-hosted checkout references and signed
/// webhook processing. All provider communication is inbound webhooks plus
/// provider-hosted checkout links: Argoscope never calls the provider, never
/// handles card data, and stores provider references and subscription state
/// only. Webhook handling is transactional (one SaveAsync per delivery) and
/// idempotent by provider event id.
/// </summary>
public sealed class BillingService
{
    private readonly IBillingStore _store;
    private readonly IEntitlementCache _cache;
    private readonly BillingOptions _options;
    private readonly Identity.IdentityOptions _identity;

    public BillingService(
        IBillingStore store,
        IEntitlementCache cache,
        IOptions<BillingOptions> options,
        IOptions<Identity.IdentityOptions> identity)
    {
        _store = store;
        _cache = cache;
        _options = options.Value;
        _identity = identity.Value;
    }

    public bool BillingEnforced => _identity.IsHosted;

    public PlanDefinition? FindPlan(string planId) =>
        _options.Plans.FirstOrDefault(p =>
            string.Equals(p.PlanId, planId, StringComparison.OrdinalIgnoreCase));

    /// <summary>Explicit trial provisioning for a new hosted tenant. No hidden
    /// default: the trial plan must be configured, otherwise nothing is
    /// provisioned and the account reports as unconfigured.</summary>
    public async Task<BillingStatusDto?> EnsureTrialAsync(
        Id<Tenant> tenantId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var plan = FindPlan(_options.TrialPlanId);
        if (plan is null) return null;

        var existing = await _store.FindSubscriptionByTenantAsync(tenantId, cancellationToken).ConfigureAwait(false);
        if (existing is not null) return ToStatus(tenantId, existing, now);

        var customer = await _store.FindCustomerByTenantAsync(tenantId, cancellationToken).ConfigureAwait(false);
        if (customer is null)
        {
            customer = new BillingCustomer(tenantId, $"local-{tenantId.Value:N}", now);
            await _store.AddCustomerAsync(customer, cancellationToken).ConfigureAwait(false);
        }

        var subscription = new BillingSubscription(
            tenantId, $"local-sub-{tenantId.Value:N}", plan.PlanId,
            SubscriptionStatus.Trial, now.AddDays(_options.TrialDays <= 0 ? 14 : _options.TrialDays), now);
        await _store.AddSubscriptionAsync(subscription, cancellationToken).ConfigureAwait(false);
        await _store.SaveAsync(cancellationToken).ConfigureAwait(false);
        return ToStatus(tenantId, subscription, now);
    }

    public async Task<BillingStatusDto?> GetStatusAsync(
        Id<Tenant> tenantId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var subscription = await _store.FindSubscriptionByTenantAsync(tenantId, cancellationToken).ConfigureAwait(false);
        return subscription is null ? null : ToStatus(tenantId, subscription, now);
    }

    /// <summary>Entitlement snapshot for enforcement boundaries. Null
    /// entitlements means billing is unconfigured for the tenant: allowed.</summary>
    public async Task<(int? Version, EntitlementDto? Entitlements)> GetEntitlementsAsync(
        Id<Tenant> tenantId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var subscription = await _store.FindSubscriptionByTenantAsync(tenantId, cancellationToken).ConfigureAwait(false);
        if (subscription is null) return (null, null);
        return (subscription.Version, ResolveEntitlements(tenantId, subscription, now));
    }

    /// <summary>Owner starts provider-hosted checkout. Stores provider
    /// references only; payment entry happens on the provider page.</summary>
    public async Task<Result<CheckoutDto>> StartCheckoutAsync(
        Id<Tenant> tenantId, string actorSubject, TenantRole actorRole,
        string? planId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (actorRole != TenantRole.Owner)
        {
            return Error.Forbidden("Checkout requires the Owner role.");
        }

        var plan = string.IsNullOrWhiteSpace(planId) ? FindPlan(_options.TrialPlanId) : FindPlan(planId.Trim());
        if (plan is null)
        {
            return Error.Validation("Plan is not in the configured catalog.");
        }

        var customer = await _store.FindCustomerByTenantAsync(tenantId, cancellationToken).ConfigureAwait(false);
        if (customer is null)
        {
            customer = new BillingCustomer(tenantId, $"local-{tenantId.Value:N}", now);
            await _store.AddCustomerAsync(customer, cancellationToken).ConfigureAwait(false);
            await _store.SaveAsync(cancellationToken).ConfigureAwait(false);
        }

        var url = $"{_options.CheckoutBaseUrl.TrimEnd('/')}?customer={Uri.EscapeDataString(customer.ProviderCustomerId)}&tenant={tenantId.Value:D}&plan={Uri.EscapeDataString(plan.PlanId)}";
        return new CheckoutDto(tenantId.Value, customer.ProviderCustomerId, url);
    }

    /// <summary>Owner records cancellation intent locally (cancel at period
    /// end). The provider confirms via webhook; reconciliation reports drift.
    /// User data is retained.</summary>
    public async Task<Result<BillingStatusDto>> RequestCancellationAsync(
        Id<Tenant> tenantId, TenantRole actorRole, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (actorRole != TenantRole.Owner)
        {
            return Error.Forbidden("Cancellation requires the Owner role.");
        }

        var subscription = await _store.FindSubscriptionByTenantAsync(tenantId, cancellationToken).ConfigureAwait(false);
        if (subscription is null)
        {
            return Error.NotFound("No subscription is configured for this tenant.");
        }

        subscription.RequestCancellation(now);
        await _store.SaveAsync(cancellationToken).ConfigureAwait(false);
        return ToStatus(tenantId, subscription, now);
    }

    public async Task<IReadOnlyList<BillingEventDto>> ListEventsAsync(
        Id<Tenant> tenantId, CancellationToken cancellationToken)
    {
        var events = await _store.ListEventsByTenantAsync(tenantId, cancellationToken).ConfigureAwait(false);
        return events.Select(BillingDtos.ToDto).ToList();
    }

    /// <summary>
    /// Verify and process one signed provider delivery. Envelope contract
    /// (billing-webhook-v1, JSON):
    /// { "id", "type", "created": unix, "data": { "customer",
    /// "subscription", "plan", "status", "current_period_end": unix,
    /// "cancel_at_period_end": bool, "tenant_id": guid-optional } }.
    /// Returns 2xx only after the event is durably recorded; duplicates return
    /// 2xx with no repeated effect; invalid signatures/timestamps are
    /// rejected with no state change.
    /// </summary>
    public async Task<Result<WebhookOutcome>> ProcessWebhookAsync(
        string rawBody, string? signatureHeader, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.WebhookSecret))
        {
            return Error.Unavailable("Billing webhooks are not configured; deliveries fail closed.");
        }

        if (!WebhookSigner.TryParseHeader(signatureHeader, out var timestamp, out var signature))
        {
            return Error.Validation("Signature header is missing or malformed.");
        }

        WebhookEnvelope envelope;
        try
        {
            envelope = WebhookEnvelope.Parse(rawBody);
        }
        catch (InvalidOperationException ex)
        {
            return Error.Validation(ex.Message);
        }

        // Replay protection: the delivery timestamp (header "t", else the
        // envelope "created") must be within ±tolerance of now.
        var effectiveTimestamp = timestamp;
        if (string.IsNullOrEmpty(effectiveTimestamp) && envelope.CreatedUnix.HasValue)
        {
            effectiveTimestamp = envelope.CreatedUnix.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        if (!long.TryParse(effectiveTimestamp, out var deliveredUnix))
        {
            return Error.Validation("Delivery timestamp is missing or invalid.");
        }

        var tolerance = TimeSpan.FromMinutes(_options.TimestampToleranceMinutes <= 0 ? 5 : _options.TimestampToleranceMinutes);
        if (Math.Abs((now - DateTimeOffset.FromUnixTimeSeconds(deliveredUnix)).TotalMinutes)
            > tolerance.TotalMinutes)
        {
            return Error.Validation("Delivery timestamp is outside the tolerance window.");
        }

        if (!WebhookSigner.Verify(_options.WebhookSecret, effectiveTimestamp, rawBody, signature))
        {
            return Error.Validation("Signature verification failed.");
        }

        var duplicate = await _store.FindEventAsync(envelope.Id, cancellationToken).ConfigureAwait(false);
        if (duplicate is not null)
        {
            return new WebhookOutcome(
                duplicate.ProviderEventId, Duplicate: true,
                Applied: duplicate.State == ProviderEventState.Applied,
                duplicate.State.ToString());
        }

        var tenantId = await ResolveTenantAsync(envelope, cancellationToken).ConfigureAwait(false);
        if (tenantId is null)
        {
            // Unknown provider mapping or cross-tenant mismatch: durably
            // ignore so provider retries do not storm, and touch nothing.
            var ignored = new ProviderEvent(
                envelope.Id, envelope.Type, null, envelope.Customer, envelope.Subscription,
                envelope.Plan, null, null, false, envelope.ProviderCreatedAt(now),
                ProviderEventState.Ignored, "Provider ids do not match any tenant mapping.",
                now);
            await _store.AddEventAsync(ignored, cancellationToken).ConfigureAwait(false);
            await _store.SaveAsync(cancellationToken).ConfigureAwait(false);
            return new WebhookOutcome(ignored.ProviderEventId, Duplicate: false, Applied: false, "Ignored");
        }

        if (!IsStateChanging(envelope.Type))
        {
            var unknown = new ProviderEvent(
                envelope.Id, envelope.Type, tenantId, envelope.Customer, envelope.Subscription,
                envelope.Plan, null, null, false, envelope.ProviderCreatedAt(now),
                ProviderEventState.Ignored, $"Unknown event type '{envelope.Type}'.",
                now);
            await _store.AddEventAsync(unknown, cancellationToken).ConfigureAwait(false);
            await _store.SaveAsync(cancellationToken).ConfigureAwait(false);
            return new WebhookOutcome(unknown.ProviderEventId, Duplicate: false, Applied: false, "Ignored");
        }

        var status = MapStatus(envelope);
        if (status is null)
        {
            var unmapped = new ProviderEvent(
                envelope.Id, envelope.Type, tenantId, envelope.Customer, envelope.Subscription,
                envelope.Plan, null, null, false, envelope.ProviderCreatedAt(now),
                ProviderEventState.Ignored, $"Unmapped subscription status '{envelope.Status}'.",
                now);
            await _store.AddEventAsync(unmapped, cancellationToken).ConfigureAwait(false);
            await _store.SaveAsync(cancellationToken).ConfigureAwait(false);
            return new WebhookOutcome(unmapped.ProviderEventId, Duplicate: false, Applied: false, "Ignored");
        }

        var planId = string.IsNullOrWhiteSpace(envelope.Plan) ? _options.TrialPlanId : envelope.Plan!.Trim();
        var subscription = string.IsNullOrWhiteSpace(envelope.Subscription)
            ? await _store.FindSubscriptionByTenantAsync(tenantId.Value, cancellationToken).ConfigureAwait(false)
            : await _store.FindSubscriptionByProviderIdAsync(envelope.Subscription!, cancellationToken).ConfigureAwait(false);

        ProviderEvent recorded;
        if (subscription is null)
        {
            subscription = new BillingSubscription(
                tenantId.Value,
                string.IsNullOrWhiteSpace(envelope.Subscription) ? $"local-sub-{tenantId.Value.Value:N}" : envelope.Subscription!.Trim(),
                planId, status.Value,
                envelope.PeriodEndUtc ?? now,
                now);
            // First event seeds LastApplied*; ApplyEvent would bump Version to
            // 2, so set the baseline directly through a same-timestamp apply.
            subscription.ApplyEvent(
                envelope.Id, planId, status.Value,
                envelope.PeriodEndUtc ?? now, envelope.CancelAtPeriodEnd,
                envelope.ProviderCreatedAt(now), now);
            await _store.AddSubscriptionAsync(subscription, cancellationToken).ConfigureAwait(false);
            recorded = new ProviderEvent(
                envelope.Id, envelope.Type, tenantId, envelope.Customer, envelope.Subscription,
                planId, status, envelope.PeriodEndUtc, envelope.CancelAtPeriodEnd,
                envelope.ProviderCreatedAt(now), ProviderEventState.Applied, null, now);
            await _store.AddEventAsync(recorded, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            if (subscription.TenantId != tenantId.Value)
            {
                var mismatch = new ProviderEvent(
                    envelope.Id, envelope.Type, tenantId, envelope.Customer, envelope.Subscription,
                    planId, status, envelope.PeriodEndUtc, envelope.CancelAtPeriodEnd,
                    envelope.ProviderCreatedAt(now), ProviderEventState.Ignored,
                    "Provider subscription maps to a different tenant.", now);
                await _store.AddEventAsync(mismatch, cancellationToken).ConfigureAwait(false);
                await _store.SaveAsync(cancellationToken).ConfigureAwait(false);
                return new WebhookOutcome(mismatch.ProviderEventId, Duplicate: false, Applied: false, "Ignored");
            }

            var applied = subscription.ApplyEvent(
                envelope.Id, planId, status.Value,
                envelope.PeriodEndUtc ?? subscription.CurrentPeriodEndUtc, envelope.CancelAtPeriodEnd,
                envelope.ProviderCreatedAt(now), now);
            recorded = new ProviderEvent(
                envelope.Id, envelope.Type, tenantId, envelope.Customer, envelope.Subscription,
                planId, status, envelope.PeriodEndUtc, envelope.CancelAtPeriodEnd,
                envelope.ProviderCreatedAt(now),
                applied ? ProviderEventState.Applied : ProviderEventState.Ignored,
                applied ? null : "Out-of-order delivery: newer state already applied.",
                now);
            await _store.AddEventAsync(recorded, cancellationToken).ConfigureAwait(false);
        }

        await _store.SaveAsync(cancellationToken).ConfigureAwait(false);
        return new WebhookOutcome(
            recorded.ProviderEventId, Duplicate: false,
            Applied: recorded.State == ProviderEventState.Applied,
            recorded.State.ToString());
    }

    /// <summary>Daily reconciliation: compare persisted subscription state
    /// against applied provider events and report mismatches. Never silently
    /// overwrites tenant data.</summary>
    public async Task<ReconciliationReport> ReconcileAsync(
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        var findings = new List<string>();
        var subscriptions = await _store.ListSubscriptionsAsync(cancellationToken).ConfigureAwait(false);
        var applied = 0;
        var ignored = 0;
        var attention = 0;

        foreach (var subscription in subscriptions)
        {
            var events = await _store.ListEventsByTenantAsync(subscription.TenantId, cancellationToken).ConfigureAwait(false);
            applied += events.Count(e => e.State == ProviderEventState.Applied);
            ignored += events.Count(e => e.State == ProviderEventState.Ignored);

            var customer = await _store.FindCustomerByTenantAsync(subscription.TenantId, cancellationToken).ConfigureAwait(false);
            if (customer is null)
            {
                attention++;
                findings.Add($"Tenant {subscription.TenantId.Value:D} has a subscription without a customer mapping.");
            }

            if (FindPlan(subscription.PlanId) is null)
            {
                attention++;
                findings.Add($"Tenant {subscription.TenantId.Value:D} is on unknown plan '{subscription.PlanId}'.");
            }

            var access = EntitlementPolicy.Normalize(
                subscription.Status, subscription.CurrentPeriodEndUtc, now, _options.PastDueGraceDays);
            if (access == EffectiveAccess.ReadOnly)
            {
                attention++;
                findings.Add($"Tenant {subscription.TenantId.Value:D} is read-only ({subscription.Status}).");
            }
        }

        return new ReconciliationReport(subscriptions.Count, applied, ignored, attention, findings);
    }

    private EntitlementDto ResolveEntitlements(Id<Tenant> tenantId, BillingSubscription subscription, DateTimeOffset now)
    {
        if (!_cache.TryGet(tenantId.Value, subscription.Version, out var snapshot) || snapshot is null)
        {
            snapshot = new EntitlementSnapshot(
                subscription.Version, subscription.PlanId, subscription.Status,
                subscription.CurrentPeriodEndUtc, subscription.CancelAtPeriodEnd);
            _cache.Set(tenantId.Value, snapshot);
        }

        var plan = FindPlan(snapshot.PlanId);
        if (plan is null)
        {
            return EntitlementPolicy.UnknownPlan(snapshot.PlanId);
        }

        var derived = new BillingSubscriptionView(
            snapshot.PlanId, snapshot.Status, snapshot.PeriodEndUtc);
        return EntitlementPolicy.DeriveFrom(plan, derived, now, _options.PastDueGraceDays);
    }

    private BillingStatusDto ToStatus(Id<Tenant> tenantId, BillingSubscription subscription, DateTimeOffset now)
    {
        var plan = FindPlan(subscription.PlanId);
        var entitlements = ResolveEntitlements(tenantId, subscription, now);
        var access = EntitlementPolicy.Normalize(
            subscription.Status, subscription.CurrentPeriodEndUtc, now, _options.PastDueGraceDays);
        return new BillingStatusDto(
            tenantId.Value,
            subscription.PlanId,
            plan?.Name ?? subscription.PlanId,
            subscription.Status.ToString(),
            access.ToString(),
            subscription.CurrentPeriodEndUtc,
            subscription.CancelAtPeriodEnd,
            subscription.Version,
            entitlements,
            _options.UpgradeUrl);
    }

    /// <summary>Resolve the owning tenant from provider ids, optionally
    /// assisted by provider-echoed tenant metadata (checkout binding). A
    /// metadata claim that contradicts a persisted mapping resolves to null
    /// (mismatch → durably ignored, nothing touched).</summary>
    private async Task<Id<Tenant>?> ResolveTenantAsync(
        WebhookEnvelope envelope, CancellationToken cancellationToken)
    {
        Id<Tenant>? bySubscription = null;
        Id<Tenant>? byCustomer = null;

        if (!string.IsNullOrWhiteSpace(envelope.Subscription))
        {
            var sub = await _store
                .FindSubscriptionByProviderIdAsync(envelope.Subscription!, cancellationToken).ConfigureAwait(false);
            if (sub is not null) bySubscription = sub.TenantId;
        }

        if (!string.IsNullOrWhiteSpace(envelope.Customer))
        {
            var customer = await _store
                .FindCustomerByProviderIdAsync(envelope.Customer!, cancellationToken).ConfigureAwait(false);
            if (customer is not null) byCustomer = customer.TenantId;
        }

        if (bySubscription.HasValue && byCustomer.HasValue && bySubscription != byCustomer)
        {
            return null;
        }

        var mapped = bySubscription ?? byCustomer;
        if (mapped.HasValue)
        {
            if (envelope.TenantId.HasValue && envelope.TenantId != mapped)
            {
                return null;
            }

            return mapped;
        }

        // First contact: bind provider ids to the metadata tenant (checkout
        // session completed for a tenant that only holds local placeholder
        // refs). Persist the binding before applying state.
        if (envelope.TenantId.HasValue)
        {
            if (!string.IsNullOrWhiteSpace(envelope.Customer) && byCustomer is null)
            {
                var existing = await _store
                    .FindCustomerByTenantAsync(envelope.TenantId.Value, cancellationToken).ConfigureAwait(false);
                if (existing is null)
                {
                    await _store.AddCustomerAsync(
                        new BillingCustomer(envelope.TenantId.Value, envelope.Customer!.Trim(), DateTimeOffset.UtcNow),
                        cancellationToken).ConfigureAwait(false);
                }
            }

            return envelope.TenantId;
        }

        return null;
    }

    private static bool IsStateChanging(string eventType)
    {
        var normalized = eventType.Trim().ToLowerInvariant();
        return normalized.Contains("subscription")
            || normalized.Contains("checkout.session.completed")
            || normalized.Contains("invoice.payment_")
            || normalized.Contains("trial");
    }

    private static SubscriptionStatus? MapStatus(WebhookEnvelope envelope)
    {
        var type = envelope.Type.Trim().ToLowerInvariant();
        if (type.Contains("past_due") || type.Contains("past-due") || type.Contains("payment_failed"))
        {
            return SubscriptionStatus.PastDue;
        }

        if (type.Contains("suspend"))
        {
            return SubscriptionStatus.Suspended;
        }

        if (type.Contains("cancel") || type.Contains("delet"))
        {
            // Canceled stays mutable through the paid-through date; the
            // policy normalizes effective access from the period end.
            return SubscriptionStatus.Canceled;
        }

        var status = (envelope.Status ?? string.Empty).Trim().ToLowerInvariant();
        return status switch
        {
            "trial" or "trialing" => SubscriptionStatus.Trial,
            "active" or "paid" or "succeeded" => SubscriptionStatus.Active,
            "past_due" or "pastdue" or "unpaid" => SubscriptionStatus.PastDue,
            "canceled" or "cancelled" or "deleted" or "ended" => SubscriptionStatus.Canceled,
            "suspended" or "paused" => SubscriptionStatus.Suspended,
            "" when type.Contains("trial") => SubscriptionStatus.Trial,
            "" when type.Contains("creat") || type.Contains("updat") || type.Contains("resum")
                || type.Contains("checkout") || type.Contains("invoice") => SubscriptionStatus.Active,
            _ => null,
        };
    }

    private sealed class WebhookEnvelope
    {
        public string Id { get; private init; } = string.Empty;
        public string Type { get; private init; } = string.Empty;
        public long? CreatedUnix { get; private init; }
        public string? Customer { get; private init; }
        public string? Subscription { get; private init; }
        public string? Plan { get; private init; }
        public string? Status { get; private init; }
        public DateTimeOffset? PeriodEndUtc { get; private init; }
        public bool CancelAtPeriodEnd { get; private init; }
        public Id<Tenant>? TenantId { get; private init; }

        public DateTimeOffset ProviderCreatedAt(DateTimeOffset fallback) =>
            CreatedUnix.HasValue
                ? DateTimeOffset.FromUnixTimeSeconds(CreatedUnix.Value)
                : fallback;

        public static WebhookEnvelope Parse(string rawBody)
        {
            using var document = JsonDocument.Parse(rawBody);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException("Webhook body must be a JSON object.");
            }

            if (!root.TryGetProperty("id", out var id) || string.IsNullOrWhiteSpace(id.GetString()))
            {
                throw new InvalidOperationException("Webhook event id is required.");
            }

            if (!root.TryGetProperty("type", out var type) || string.IsNullOrWhiteSpace(type.GetString()))
            {
                throw new InvalidOperationException("Webhook event type is required.");
            }

            long? created = null;
            if (root.TryGetProperty("created", out var createdEl) && createdEl.ValueKind == JsonValueKind.Number
                && createdEl.TryGetInt64(out var createdUnix))
            {
                created = createdUnix;
            }

            string? customer = null;
            string? subscription = null;
            string? plan = null;
            string? status = null;
            DateTimeOffset? periodEnd = null;
            var cancelAtPeriodEnd = false;
            Id<Tenant>? tenantId = null;

            if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
            {
                customer = GetString(data, "customer");
                subscription = GetString(data, "subscription");
                plan = GetString(data, "plan") ?? GetString(data, "plan_id");
                status = GetString(data, "status");
                if (data.TryGetProperty("current_period_end", out var periodEl)
                    && periodEl.ValueKind == JsonValueKind.Number
                    && periodEl.TryGetInt64(out var periodUnix))
                {
                    periodEnd = DateTimeOffset.FromUnixTimeSeconds(periodUnix);
                }

                if (data.TryGetProperty("cancel_at_period_end", out var cancelEl)
                    && (cancelEl.ValueKind == JsonValueKind.True || cancelEl.ValueKind == JsonValueKind.False))
                {
                    cancelAtPeriodEnd = cancelEl.GetBoolean();
                }

                var tenantClaim = GetString(data, "tenant_id");
                if (!string.IsNullOrWhiteSpace(tenantClaim) && Guid.TryParse(tenantClaim, out var tenantGuid))
                {
                    tenantId = Id<Tenant>.From(tenantGuid);
                }
            }

            return new WebhookEnvelope
            {
                Id = id.GetString()!.Trim(),
                Type = type.GetString()!.Trim(),
                CreatedUnix = created,
                Customer = customer,
                Subscription = subscription,
                Plan = plan,
                Status = status,
                PeriodEndUtc = periodEnd,
                CancelAtPeriodEnd = cancelAtPeriodEnd,
                TenantId = tenantId,
            };
        }

        private static string? GetString(JsonElement element, string name) =>
            element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }
}
