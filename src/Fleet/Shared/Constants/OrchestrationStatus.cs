namespace Fleet.Shared.Constants;

public static class OrchestrationStatus
{
    public const string Working = "working";

    public const string Done = "done";

    public const string Failed = "failed";

    public static IReadOnlyList<string> All { get; } = [Working, Done, Failed];

    public static string Normalize(string status) =>
        All.Contains(status.Trim().ToLowerInvariant()) ? status.Trim().ToLowerInvariant() : Working;
}
