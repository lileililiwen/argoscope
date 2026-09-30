using Argoscope.Application.Billing;
using Argoscope.Domain.Billing;
using Argoscope.Domain.Common;
using Argoscope.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace Argoscope.Infrastructure.Persistence;

/// <summary>EF billing store. Webhook handling defers every change to a single
/// SaveAsync so the event row and the subscription mutation persist
/// transactionally.</summary>
public sealed class EfBillingStore : IBillingStore
{
    private readonly ArgoscopeDbContext _db;
    public EfBillingStore(ArgoscopeDbContext db) => _db = db;

    public Task<BillingCustomer?> FindCustomerByTenantAsync(Id<Tenant> tenantId, CancellationToken cancellationToken) =>
        _db.BillingCustomers.FirstOrDefaultAsync(c => c.TenantId == tenantId, cancellationToken);

    public Task<BillingCustomer?> FindCustomerByProviderIdAsync(string providerCustomerId, CancellationToken cancellationToken) =>
        _db.BillingCustomers.FirstOrDefaultAsync(c => c.ProviderCustomerId == providerCustomerId, cancellationToken);

    public Task AddCustomerAsync(BillingCustomer customer, CancellationToken cancellationToken) =>
        _db.BillingCustomers.AddAsync(customer, cancellationToken).AsTask();

    public Task<BillingSubscription?> FindSubscriptionByTenantAsync(Id<Tenant> tenantId, CancellationToken cancellationToken) =>
        _db.BillingSubscriptions.FirstOrDefaultAsync(s => s.TenantId == tenantId, cancellationToken);

    public Task<BillingSubscription?> FindSubscriptionByProviderIdAsync(string providerSubscriptionId, CancellationToken cancellationToken) =>
        _db.BillingSubscriptions.FirstOrDefaultAsync(s => s.ProviderSubscriptionId == providerSubscriptionId, cancellationToken);

    public Task AddSubscriptionAsync(BillingSubscription subscription, CancellationToken cancellationToken) =>
        _db.BillingSubscriptions.AddAsync(subscription, cancellationToken).AsTask();

    public async Task<IReadOnlyList<BillingSubscription>> ListSubscriptionsAsync(CancellationToken cancellationToken) =>
        await _db.BillingSubscriptions.OrderBy(s => s.CreatedAtUtc).ToListAsync(cancellationToken).ConfigureAwait(false);

    public Task<ProviderEvent?> FindEventAsync(string providerEventId, CancellationToken cancellationToken) =>
        _db.ProviderEvents.FirstOrDefaultAsync(e => e.ProviderEventId == providerEventId, cancellationToken);

    public Task AddEventAsync(ProviderEvent @event, CancellationToken cancellationToken) =>
        _db.ProviderEvents.AddAsync(@event, cancellationToken).AsTask();

    public async Task<IReadOnlyList<ProviderEvent>> ListEventsByTenantAsync(Id<Tenant> tenantId, CancellationToken cancellationToken) =>
        await _db.ProviderEvents
            .Where(e => e.TenantId == tenantId)
            .OrderBy(e => e.ReceivedAtUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public Task SaveAsync(CancellationToken cancellationToken) =>
        _db.SaveChangesAsync(cancellationToken);
}

public sealed class BillingCustomerConfiguration : IEntityTypeConfiguration<BillingCustomer>
{
    public void Configure(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<BillingCustomer> b)
    {
        b.ToTable("billing_customers");
        b.HasKey(c => c.Id);
        b.Property(c => c.Id).HasConversion(v => v.Value, v => Id<BillingCustomer>.From(v));
        b.Property(c => c.TenantId).HasConversion(
            v => v.Value, v => Id<Tenant>.From(v));
        b.HasIndex(c => c.TenantId).IsUnique();
        b.Property(c => c.ProviderCustomerId).HasMaxLength(BillingCustomer.MaxProviderIdLength).IsRequired();
        b.HasIndex(c => c.ProviderCustomerId).IsUnique();
        b.Property(c => c.CreatedAtUtc).IsRequired();
        b.Property(c => c.UpdatedAtUtc).IsRequired();
    }
}

public sealed class BillingSubscriptionConfiguration : IEntityTypeConfiguration<BillingSubscription>
{
    public void Configure(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<BillingSubscription> b)
    {
        b.ToTable("billing_subscriptions");
        b.HasKey(s => s.Id);
        b.Property(s => s.Id).HasConversion(v => v.Value, v => Id<BillingSubscription>.From(v));
        b.Property(s => s.TenantId).HasConversion(
            v => v.Value, v => Id<Tenant>.From(v));
        b.HasIndex(s => s.TenantId).IsUnique();
        b.Property(s => s.ProviderSubscriptionId).HasMaxLength(BillingSubscription.MaxProviderIdLength).IsRequired();
        b.HasIndex(s => s.ProviderSubscriptionId).IsUnique();
        b.Property(s => s.PlanId).HasMaxLength(BillingSubscription.MaxPlanIdLength).IsRequired();
        b.Property(s => s.Status).HasConversion<int>();
        b.Property(s => s.CurrentPeriodEndUtc).IsRequired();
        b.Property(s => s.CancelAtPeriodEnd).IsRequired();
        b.Property(s => s.Version).IsRequired();
        b.Property(s => s.LastAppliedEventId).HasMaxLength(BillingSubscription.MaxEventIdLength);
        b.Property(s => s.LastAppliedEventAtUtc);
        b.Property(s => s.CreatedAtUtc).IsRequired();
        b.Property(s => s.UpdatedAtUtc).IsRequired();
    }
}

public sealed class ProviderEventConfiguration : IEntityTypeConfiguration<ProviderEvent>
{
    public void Configure(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<ProviderEvent> b)
    {
        b.ToTable("provider_events");
        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasConversion(v => v.Value, v => Id<ProviderEvent>.From(v));
        b.Property(e => e.ProviderEventId).HasMaxLength(ProviderEvent.MaxEventIdLength).IsRequired();
        b.HasIndex(e => e.ProviderEventId).IsUnique();
        b.Property(e => e.EventType).HasMaxLength(ProviderEvent.MaxEventTypeLength).IsRequired();
        b.Property(e => e.TenantId).HasConversion(
            v => v.HasValue ? v.Value.Value : (Guid?)null,
            v => v.HasValue ? Id<Tenant>.From(v.Value) : (Id<Tenant>?)null);
        b.HasIndex(e => e.TenantId);
        b.Property(e => e.ProviderCustomerId).HasMaxLength(ProviderEvent.MaxProviderIdLength);
        b.Property(e => e.ProviderSubscriptionId).HasMaxLength(ProviderEvent.MaxProviderIdLength);
        b.Property(e => e.PlanId).HasMaxLength(ProviderEvent.MaxPlanIdLength);
        b.Property(e => e.Status).HasConversion<int>();
        b.Property(e => e.CurrentPeriodEndUtc);
        b.Property(e => e.CancelAtPeriodEnd).IsRequired();
        b.Property(e => e.ProviderCreatedAtUtc).IsRequired();
        b.Property(e => e.State).HasConversion<int>();
        b.Property(e => e.Note).HasMaxLength(500);
        b.Property(e => e.ReceivedAtUtc).IsRequired();
    }
}
