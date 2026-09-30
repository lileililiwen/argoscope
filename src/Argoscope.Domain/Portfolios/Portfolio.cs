using Argoscope.Domain.Common;

namespace Argoscope.Domain.Portfolios;

/// <summary>
/// Self-hosted single-owner portfolio. The MVP keeps one owner per Argoscope
/// instance; multi-tenant is deferred to a later change.
/// </summary>
public sealed class Portfolio : Entity<Id<Portfolio>>
{
    public string Name { get; private set; }

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
