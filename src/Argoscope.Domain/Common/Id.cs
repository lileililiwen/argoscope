namespace Argoscope.Domain.Common;

/// <summary>Stable typed identifier. Entity ids use <see cref="Guid"/>.</summary>
public readonly record struct Id<T>(Guid Value) where T : notnull
{
    public static Id<T> New() => new(Guid.NewGuid());

    public static Id<T> From(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}
