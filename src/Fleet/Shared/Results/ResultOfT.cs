namespace Fleet.Shared.Results;

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
