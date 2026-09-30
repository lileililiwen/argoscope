using Argoscope.Domain.Billing;

namespace Argoscope.Application.Billing;

/// <summary>Normalized account access after grace and paid-through rules.</summary>
public enum EffectiveAccess
{
    Full,
    ReadOnly,
}

/// <summary>Time-independent subscription view for entitlement derivation.</summary>
public sealed record BillingSubscriptionView(
    string PlanId,
    Domain.Billing.SubscriptionStatus Status,
    DateTimeOffset PeriodEndUtc);

/// <summary>
/// Derives tenant-scoped entitlements from plan + normalized subscription
/// state. PastDue stays fully entitled through the grace window; Canceled
/// stays fully entitled through the paid-through date; Suspended and lapsed
/// accounts are read-only. Data is always retained.
/// </summary>
public static class EntitlementPolicy
{
    public static EffectiveAccess Normalize(SubscriptionStatus status, DateTimeOffset? periodEndUtc, DateTimeOffset now, int pastDueGraceDays)
    {
        return status switch
        {
            SubscriptionStatus.Trial => EffectiveAccess.Full,
            SubscriptionStatus.Active => EffectiveAccess.Full,
            SubscriptionStatus.PastDue => now <= periodEndUtc.GetValueOrDefault(now).AddDays(pastDueGraceDays)
                ? EffectiveAccess.Full
                : EffectiveAccess.ReadOnly,
            SubscriptionStatus.Canceled => periodEndUtc.HasValue && now <= periodEndUtc.Value
                ? EffectiveAccess.Full
                : EffectiveAccess.ReadOnly,
            SubscriptionStatus.Suspended => EffectiveAccess.ReadOnly,
            _ => EffectiveAccess.ReadOnly,
        };
    }

    public static string? ReadOnlyReason(SubscriptionStatus status, DateTimeOffset? periodEndUtc, DateTimeOffset now, int pastDueGraceDays)
    {
        if (Normalize(status, periodEndUtc, now, pastDueGraceDays) == EffectiveAccess.Full) return null;
        return status switch
        {
            SubscriptionStatus.PastDue => "Subscription is past due and the grace window has elapsed.",
            SubscriptionStatus.Canceled => "Subscription is canceled and the paid-through period has ended.",
            SubscriptionStatus.Suspended => "Account is suspended.",
            _ => "Subscription is not active.",
        };
    }

    public static EntitlementDto Derive(
        PlanDefinition plan, BillingSubscription subscription, DateTimeOffset now, int pastDueGraceDays) =>
        DeriveFrom(
            plan,
            new BillingSubscriptionView(subscription.PlanId, subscription.Status, subscription.CurrentPeriodEndUtc),
            now, pastDueGraceDays);

    public static EntitlementDto DeriveFrom(
        PlanDefinition plan, BillingSubscriptionView subscription, DateTimeOffset now, int pastDueGraceDays)
    {
        var access = Normalize(subscription.Status, subscription.PeriodEndUtc, now, pastDueGraceDays);
        return new EntitlementDto(
            plan.PlanId,
            plan.Name,
            access == EffectiveAccess.Full,
            plan.MaxPortfolios,
            plan.IncludesAdvancedAnalytics && access == EffectiveAccess.Full,
            plan.IncludesAlerts && access == EffectiveAccess.Full,
            ReadOnlyReason(subscription.Status, subscription.PeriodEndUtc, now, pastDueGraceDays));
    }

    /// <summary>Fallback entitlements for a plan that is no longer in the
    /// catalog: read-only so an unknown plan can never grant access.</summary>
    public static EntitlementDto UnknownPlan(string planId) =>
        new(planId, planId, false, 0, false, false, "Plan is not in the configured catalog.");
}

/// <summary>Time-independent subscription snapshot held by the entitlement
/// cache. Entitlements are derived from the snapshot plus the current time on
/// every read, so grace windows and paid-through dates stay exact even when
/// the subscription row (and its version) has not changed.</summary>
public sealed record EntitlementSnapshot(
    int Version,
    string PlanId,
    Domain.Billing.SubscriptionStatus Status,
    DateTimeOffset PeriodEndUtc,
    bool CancelAtPeriodEnd);

/// <summary>Tenant-scoped versioned entitlement cache. Entries are keyed by
/// tenant and carry the subscription version they were snapshotted from; a
/// version mismatch forces a reload, so every applied provider event
/// implicitly invalidates the cache. Derivation from the snapshot is pure
/// computation, keeping time-dependent access exact on every read.</summary>
public interface IEntitlementCache
{
    bool TryGet(Guid tenantId, int version, out EntitlementSnapshot? snapshot);
    void Set(Guid tenantId, EntitlementSnapshot snapshot);
}

public sealed class InMemoryEntitlementCache : IEntitlementCache
{
    private readonly Dictionary<Guid, EntitlementSnapshot> _entries = new();
    private readonly object _lock = new();

    public bool TryGet(Guid tenantId, int version, out EntitlementSnapshot? snapshot)
    {
        lock (_lock)
        {
            if (_entries.TryGetValue(tenantId, out var entry) && entry.Version == version)
            {
                snapshot = entry;
                return true;
            }
        }

        snapshot = null;
        return false;
    }

    public void Set(Guid tenantId, EntitlementSnapshot snapshot)
    {
        lock (_lock)
        {
            _entries[tenantId] = snapshot;
        }
    }
}
