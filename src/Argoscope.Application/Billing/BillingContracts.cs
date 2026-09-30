using Argoscope.Domain.Billing;
using Argoscope.Domain.Common;
using Argoscope.Domain.Identity;

namespace Argoscope.Application.Billing;

/// <summary>Entitlements derived from the tenant plan + normalized status.
/// Never carries payment method details.</summary>
public sealed record EntitlementDto(
    string PlanId,
    string PlanName,
    bool CanMutate,
    int MaxPortfolios,
    bool CanUseAdvancedAnalytics,
    bool CanUseAlerts,
    string? ReadOnlyReason);

/// <summary>Public billing status for the account surface. Exposes plan,
/// status, period end, cancellation flag and entitlements only.</summary>
public sealed record BillingStatusDto(
    Guid TenantId,
    string PlanId,
    string PlanName,
    string Status,
    string EffectiveAccess,
    DateTimeOffset CurrentPeriodEndUtc,
    bool CancelAtPeriodEnd,
    int Version,
    EntitlementDto Entitlements,
    string UpgradeUrl);

/// <summary>Provider-hosted checkout reference. The URL is provider-hosted
/// payment entry; Argoscope stores provider references only.</summary>
public sealed record CheckoutDto(
    Guid TenantId,
    string ProviderCustomerId,
    string CheckoutUrl);

public sealed record BillingEventDto(
    string ProviderEventId,
    string EventType,
    string State,
    string? PlanId,
    string? Status,
    DateTimeOffset ProviderCreatedAtUtc,
    DateTimeOffset ReceivedAtUtc,
    string? Note);

public sealed record WebhookOutcome(
    string ProviderEventId,
    bool Duplicate,
    bool Applied,
    string State);

public sealed record ReconciliationReport(
    int Checked,
    int AppliedEvents,
    int IgnoredEvents,
    int Attention,
    IReadOnlyList<string> Findings);

public static class BillingDtos
{
    public static BillingEventDto ToDto(ProviderEvent e) =>
        new(
            e.ProviderEventId,
            e.EventType,
            e.State.ToString(),
            e.PlanId,
            e.Status?.ToString(),
            e.ProviderCreatedAtUtc,
            e.ReceivedAtUtc,
            e.Note);
}

/// <summary>Combined billing persistence. Webhook handling persists the event
/// row and the subscription mutation with a single SaveAsync so signature
/// verification, idempotency and state transitions stay transactional.</summary>
public interface IBillingStore
{
    Task<BillingCustomer?> FindCustomerByTenantAsync(Id<Tenant> tenantId, CancellationToken cancellationToken);
    Task<BillingCustomer?> FindCustomerByProviderIdAsync(string providerCustomerId, CancellationToken cancellationToken);
    Task AddCustomerAsync(BillingCustomer customer, CancellationToken cancellationToken);

    Task<BillingSubscription?> FindSubscriptionByTenantAsync(Id<Tenant> tenantId, CancellationToken cancellationToken);
    Task<BillingSubscription?> FindSubscriptionByProviderIdAsync(string providerSubscriptionId, CancellationToken cancellationToken);
    Task AddSubscriptionAsync(BillingSubscription subscription, CancellationToken cancellationToken);
    Task<IReadOnlyList<BillingSubscription>> ListSubscriptionsAsync(CancellationToken cancellationToken);

    Task<ProviderEvent?> FindEventAsync(string providerEventId, CancellationToken cancellationToken);
    Task AddEventAsync(ProviderEvent @event, CancellationToken cancellationToken);
    Task<IReadOnlyList<ProviderEvent>> ListEventsByTenantAsync(Id<Tenant> tenantId, CancellationToken cancellationToken);

    /// <summary>Durably record all pending changes. Webhook processing calls
    /// this once per delivery; a failure leaves no event row behind so the
    /// provider retry (same event id) can be processed cleanly as 5xx.</summary>
    Task SaveAsync(CancellationToken cancellationToken);
}
