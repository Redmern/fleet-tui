namespace Fleet.Shared;

/// <summary>
/// An operation that can fail in an expected way. Failure is part of the
/// signature rather than an exception thrown from somewhere unseen; exceptions
/// stay reserved for defects.
/// </summary>
public readonly struct Result<T>
{
    private readonly T _value;

    private Result(bool succeeded, T value, string? error)
    {
        Succeeded = succeeded;
        _value = value;
        Error = error;
    }

    public bool Succeeded { get; }

    public string? Error { get; }

    /// <summary>
    /// Throws when the result failed. Reading a value without checking
    /// <see cref="Succeeded"/> is a defect, and a silent default would hide it.
    /// </summary>
    public T Value => Succeeded
        ? _value
        : throw new InvalidOperationException($"Result failed: {Error}");

    public static Result<T> Ok(T value) => new(true, value, null);

    public static Result<T> Fail(string error)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            throw new ArgumentException("A failure needs a reason", nameof(error));
        }

        return new Result<T>(false, default!, error);
    }
}

/// <summary>A result with no payload.</summary>
public readonly struct Result
{
    private Result(bool succeeded, string? error)
    {
        Succeeded = succeeded;
        Error = error;
    }

    public bool Succeeded { get; }

    public string? Error { get; }

    public static Result Ok() => new(true, null);

    public static Result Fail(string error)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            throw new ArgumentException("A failure needs a reason", nameof(error));
        }

        return new Result(false, error);
    }
}
