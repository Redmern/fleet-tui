namespace Fleet.Shared.Results;

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
