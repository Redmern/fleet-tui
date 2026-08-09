namespace Fleet.Shared.Constants;

public static class AgentHarness
{
    public const string Claude = "claude";

    public const string Nvim = "nvim";

    public static IReadOnlyList<string> All { get; } = [Claude, Nvim];

    public static string Describe(string harness) => harness switch
    {
        Claude => "claude",
        Nvim => "nvim (claude from inside it)",
        _ => harness,
    };

    public static string Normalize(string harness) =>
        All.Contains(harness.Trim()) ? harness.Trim() : Claude;
}
