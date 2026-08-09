namespace Fleet.Shared.Constants;

public static class AgentHarness
{
    public const string Claude = "claude";

    public const string Nvim = "nvim";

    public const string NvimStartup =
        "lua vim.schedule(function() vim.cmd('Neotree show') vim.cmd('ClaudeCode') end)";

    public static IReadOnlyList<string> All { get; } = [Claude, Nvim];

    public static string Describe(string harness) => harness switch
    {
        Claude => "claude",
        Nvim => "nvim (neo-tree and claude)",
        _ => harness,
    };

    public static IReadOnlyList<string> CommandFor(string harness) =>
        Normalize(harness) == Nvim
            ? [Nvim, "-c", NvimStartup]
            : [Claude];

    public static string Normalize(string harness) =>
        All.Contains(harness.Trim()) ? harness.Trim() : Claude;
}
