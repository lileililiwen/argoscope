using Argoscope.Domain.Common;

namespace Argoscope.Domain.Billing;

/// <summary>Normalized subscription lifecycle. Trial and Active can mutate;
/// PastDue has a 7-day grace window; Canceled stays mutable through the
/// paid-through date; Suspended is read-only. Data is never auto-deleted by
/// billing transitions.</summary>
public enum SubscriptionStatus
{
    Trial = 0,
    Active = 1,
    PastDue = 2,
    Canceled = 3,
    Suspended = 4,
}

/// <summary>Durable provider-event handling state. Every verified delivery is
/// recorded exactly once; duplicates, out-of-order deliveries and unknown
/// payloads are recorded as Ignored so provider retries stay safe.</summary>
public enum ProviderEventState
{
    Received = 0,
    Applied = 1,
    Ignored = 2,
    Failed = 3,
}

/// <summary>
/// Provider-side customer reference for one tenant. Stores provider
/// references only; Argoscope never handles or stores card numbers, security
/// codes or any other raw payment credentials.
/// </summary>
public sealed class BillingCustomer : Entity<Id<BillingCustomer>>
{
    public const int MaxProviderIdLength = 200;

    public Id<Identity.Tenant> TenantId { get; private set; }

    public string ProviderCustomerId { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    private BillingCustomer() : base()
    {
        TenantId = default!;
        ProviderCustomerId = string.Empty;
    }

    public BillingCustomer(Id<Identity.Tenant> tenantId, string providerCustomerId, DateTimeOffset now)
        : base(Id<BillingCustomer>.New())
    {
        if (string.IsNullOrWhiteSpace(providerCustomerId))
        {
            throw new DomainException("validation", "Provider customer id is required.");
        }

        TenantId = tenantId;
        ProviderCustomerId = providerCustomerId.Trim();
        CreatedAtUtc = now;
        UpdatedAtUtc = now;
    }
}

/// <summary>
/// Tenant subscription state. Exactly one row per tenant. Version increments
/// on every applied provider event so entitlement caches invalidate by
/// tenant-scoped version key. LastAppliedEventAtUtc guards out-of-order
/// deliveries: older provider timestamps never overwrite newer state.
/// </summary>
public sealed class BillingSubscription : Entity<Id<BillingSubscription>>
{
    public const int MaxPlanIdLength = 100;
    public const int MaxProviderIdLength = 200;
    public const int MaxEventIdLength = 200;

    public Id<Identity.Tenant> TenantId { get; private set; }

    public string ProviderSubscriptionId { get; private set; }

    public string PlanId { get; private set; }

    public SubscriptionStatus Status { get; private set; }

    public DateTimeOffset CurrentPeriodEndUtc { get; private set; }

    public bool CancelAtPeriodEnd { get; private set; }

    public int Version { get; private set; }

    public string? LastAppliedEventId { get; private set; }

    public DateTimeOffset? LastAppliedEventAtUtc { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    private BillingSubscription() : base()
    {
        TenantId = default!;
        ProviderSubscriptionId = string.Empty;
        PlanId = string.Empty;
    }

    public BillingSubscription(
        Id<Identity.Tenant> tenantId,
        string providerSubscriptionId,
        string planId,
        SubscriptionStatus status,
        DateTimeOffset currentPeriodEndUtc,
        DateTimeOffset now)
        : base(Id<BillingSubscription>.New())
    {
        if (string.IsNullOrWhiteSpace(providerSubscriptionId))
        {
            throw new DomainException("validation", "Provider subscription id is required.");
        }

        if (string.IsNullOrWhiteSpace(planId))
        {
            throw new DomainException("validation", "Plan id is required.");
        }

        TenantId = tenantId;
        ProviderSubscriptionId = providerSubscriptionId.Trim();
        PlanId = planId.Trim();
        Status = status;
        CurrentPeriodEndUtc = currentPeriodEndUtc;
        Version = 1;
        CreatedAtUtc = now;
        UpdatedAtUtc = now;
    }

    /// <summary>Apply a verified provider event. Returns false when the event
    /// is older than the last applied event (out-of-order delivery): the
    /// caller must record it as Ignored and keep the newer state.</summary>
    public bool ApplyEvent(
        string providerEventId,
        string planId,
        SubscriptionStatus status,
        DateTimeOffset currentPeriodEndUtc,
        bool cancelAtPeriodEnd,
        DateTimeOffset providerCreatedAtUtc,
        DateTimeOffset now)
    {
        if (LastAppliedEventAtUtc.HasValue && providerCreatedAtUtc < LastAppliedEventAtUtc.Value)
        {
            return false;
        }

        PlanId = planId.Trim();
        Status = status;
        CurrentPeriodEndUtc = currentPeriodEndUtc;
        CancelAtPeriodEnd = cancelAtPeriodEnd;
        Version += 1;
        LastAppliedEventId = providerEventId;
        LastAppliedEventAtUtc = providerCreatedAtUtc;
        UpdatedAtUtc = now;
        return true;
    }

    public void RequestCancellation(DateTimeOffset now)
    {
        CancelAtPeriodEnd = true;
        UpdatedAtUtc = now;
    }
}

/// <summary>
/// Durable record of one provider delivery, unique by provider event id.
/// Payload details beyond routing/plan fields are intentionally not stored:
/// only the references needed for idempotent replay and audit.
/// </summary>
public sealed class ProviderEvent : Entity<Id<ProviderEvent>>
{
    public const int MaxEventIdLength = 200;
    public const int MaxEventTypeLength = 100;
    public const int MaxProviderIdLength = 200;
    public const int MaxPlanIdLength = 100;

    public string ProviderEventId { get; private set; }

    public string EventType { get; private set; }

    public Id<Identity.Tenant>? TenantId { get; private set; }

    public string? ProviderCustomerId { get; private set; }

    public string? ProviderSubscriptionId { get; private set; }

    public string? PlanId { get; private set; }

    public SubscriptionStatus? Status { get; private set; }

    public DateTimeOffset? CurrentPeriodEndUtc { get; private set; }

    public bool CancelAtPeriodEnd { get; private set; }

    public DateTimeOffset ProviderCreatedAtUtc { get; private set; }

    public ProviderEventState State { get; private set; }

    public string? Note { get; private set; }

    public DateTimeOffset ReceivedAtUtc { get; private set; }

    private ProviderEvent() : base()
    {
        ProviderEventId = string.Empty;
        EventType = string.Empty;
    }

    public ProviderEvent(
        string providerEventId,
        string eventType,
        Id<Identity.Tenant>? tenantId,
        string? providerCustomerId,
        string? providerSubscriptionId,
        string? planId,
        SubscriptionStatus? status,
        DateTimeOffset? currentPeriodEndUtc,
        bool cancelAtPeriodEnd,
        DateTimeOffset providerCreatedAtUtc,
        ProviderEventState state,
        string? note,
        DateTimeOffset now)
        : base(Id<ProviderEvent>.New())
    {
        if (string.IsNullOrWhiteSpace(providerEventId))
        {
            throw new DomainException("validation", "Provider event id is required.");
        }

        if (string.IsNullOrWhiteSpace(eventType))
        {
            throw new DomainException("validation", "Event type is required.");
        }

        ProviderEventId = providerEventId.Trim();
        EventType = eventType.Trim();
        TenantId = tenantId;
        ProviderCustomerId = providerCustomerId;
        ProviderSubscriptionId = providerSubscriptionId;
        PlanId = planId;
        Status = status;
        CurrentPeriodEndUtc = currentPeriodEndUtc;
        CancelAtPeriodEnd = cancelAtPeriodEnd;
        ProviderCreatedAtUtc = providerCreatedAtUtc;
        State = state;
        Note = note;
        ReceivedAtUtc = now;
    }

    public void Mark(ProviderEventState state, string? note)
    {
        State = state;
        Note = note;
    }
}
