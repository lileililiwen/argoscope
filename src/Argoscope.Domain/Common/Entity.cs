namespace Argoscope.Domain.Common;

/// <summary>
/// Marker for domain entities with a strongly-typed <see cref="Id{T}"/>.
/// </summary>
public interface IEntity<TId> where TId : struct
{
    TId Id { get; }
}

/// <summary>Base class for domain entities. Equality is by id.</summary>
public abstract class Entity<TId> : IEntity<TId> where TId : struct
{
    public TId Id { get; protected init; }

    protected Entity() { }

    protected Entity(TId id)
    {
        Id = id;
    }

    public override bool Equals(object? obj) =>
        obj is Entity<TId> other && EqualityComparer<TId>.Default.Equals(Id, other.Id);

    public override int GetHashCode() => EqualityComparer<TId>.Default.GetHashCode(Id);
}
