using Argoscope.Domain.Common;
using Argoscope.Domain.Identity;

namespace Argoscope.Domain.Portfolios;

/// <summary>
/// Self-hosted single-owner portfolio. The MVP keeps one owner per Argoscope
/// instance; hosted multi-tenant assignment is tracked via <see cref="TenantId"/>
/// (null for legacy rows until the hosted migration assigns them).
/// </summary>
public sealed class Portfolio : Entity<Id<Portfolio>>
{
    public string Name { get; private set; }

    /// <summary>Owning tenant. Null only for legacy rows created before hosted identity.</summary>
    public Id<Tenant>? TenantId { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    private Portfolio() : base() { }

    public Portfolio(string name, DateTimeOffset now)
        : base(Id<Portfolio>.New())
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("validation", "Portfolio name is required.");
        }

        Name = name.Trim();
        CreatedAtUtc = now;
        UpdatedAtUtc = now;
    }

    public void AssignTenant(Id<Tenant> tenantId, DateTimeOffset now)
    {
        TenantId = tenantId;
        UpdatedAtUtc = now;
    }

    /// <summary>
    /// Rollback-only: clears a tenant assignment applied by a failed
    /// migration attempt so the row stays visibly unscoped. Never used on
    /// success paths; hosted queries treat null as migration-required.
    /// </summary>
    public void ClearTenantForRollback(DateTimeOffset now)
    {
        TenantId = null;
        UpdatedAtUtc = now;
    }

    public void Rename(string name, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("validation", "Portfolio name is required.");
        }

        Name = name.Trim();
        UpdatedAtUtc = now;
    }
}
