namespace Argoscope.Domain.Common;

/// <summary>
/// Domain exception used for invariant violations that should not be reachable
/// from well-behaved callers; the API layer maps it to HTTP 400.
/// </summary>
public sealed class DomainException : Exception
{
    public string Code { get; }

    public DomainException(string code, string message) : base(message)
    {
        Code = code;
    }
}
