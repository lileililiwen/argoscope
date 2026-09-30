using Argoscope.Domain.Common;

namespace Argoscope.Domain.Identity;

/// <summary>
/// Hosted tenant boundary. Every portfolio row is assigned to exactly one
/// tenant after the hosted migration; tenant IDs are the mandatory query
/// context for all hosted API, job and export surfaces.
/// </summary>
public sealed class Tenant : Entity<Id<Tenant>>
{
    public const int MaxNameLength = 200;

    public string Name { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    private Tenant() : base() { Name = string.Empty; }

    public Tenant(string name, DateTimeOffset now)
        : base(Id<Tenant>.New())
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("validation", "Tenant name is required.");
        }

        Name = name.Trim();
        CreatedAtUtc = now;
        UpdatedAtUtc = now;
    }

    public void Rename(string name, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("validation", "Tenant name is required.");
        }

        Name = name.Trim();
        UpdatedAtUtc = now;
    }
}
