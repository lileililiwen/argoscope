namespace Argoscope.Domain.Common;

/// <summary>
/// Abstraction over the system clock so the application layer can be tested
/// with deterministic UTC instants. All times stored in Argoscope are UTC.
/// </summary>
public interface IClock
{
    /// <summary>Returns the current UTC instant.</summary>
    DateTimeOffset UtcNow { get; }
}

/// <summary>
/// System clock that returns the real wall clock. Used outside tests.
/// </summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
