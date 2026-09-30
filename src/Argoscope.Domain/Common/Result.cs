namespace Argoscope.Domain.Common;

/// <summary>
/// Identifies a failure mode. The HTTP layer maps <see cref="Error"/> to a
/// status code; the application layer never deals with HTTP directly.
/// </summary>
public readonly record struct Error(string Code, string Message, string? Target = null)
{
    public static Error Validation(string message, string? target = null) =>
        new("validation", message, target);

    public static Error NotFound(string message) => new("not_found", message);

    public static Error Conflict(string message) => new("conflict", message);

    public static Error Unauthorized(string message) => new("unauthorized", message);

    public static Error Forbidden(string message) => new("forbidden", message);
}

/// <summary>
/// Result of an operation that may fail with a single error. The application
/// and API layers must not throw for predictable failures; they return
/// <see cref="Result{T}"/> instead.
/// </summary>
public readonly struct Result<T>
{
    public T? Value { get; }

    public Error? Error { get; }

    public bool IsSuccess => Error is null;

    public bool IsFailure => !IsSuccess;

    private Result(T value)
    {
        Value = value;
        Error = null;
    }

    private Result(Error error)
    {
        Value = default;
        Error = error;
    }

    public static Result<T> Success(T value) => new(value);

    public static Result<T> Failure(Error error) => new(error);

    public static implicit operator Result<T>(T value) => Success(value);

    public static implicit operator Result<T>(Error error) => Failure(error);
}

/// <summary>Result without a value, used for commands.</summary>
public readonly struct Result
{
    public Error? Error { get; }

    public bool IsSuccess => Error is null;

    public bool IsFailure => Error is not null;

    private Result(Error? error) => Error = error;

    public static Result Success() => new(null);

    public static Result Failure(Error error) => new(error);

    public static implicit operator Result(Error error) => Failure(error);
}
