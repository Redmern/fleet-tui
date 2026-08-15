namespace Fleet.Shared.Constants;

public static class AgentHarness
{
    public const string Claude = "claude";

    public const string Nvim = "nvim";

    public const string Orchestrator = "orchestrator";

    public const string NvimStartup =
        "lua vim.schedule(function() vim.cmd('Neotree show') vim.cmd('ClaudeCode') end)";

    public const string ResumeArgument = "--continue";

    public const string OrchestratorKickoff =
        "Read CLAUDE.md and TASK.md in this folder, then begin.";

    public static IReadOnlyList<string> All { get; } = [Nvim, Claude];

    public static IReadOnlyList<string> Known { get; } = [Nvim, Claude, Orchestrator];

    public static string Describe(string harness) => Normalize(harness);

    public const string BrowseStartup = "lua vim.schedule(function() vim.cmd('Neotree show') end)";

    public static IReadOnlyList<string> BrowseCommand { get; } = [Nvim, "-c", BrowseStartup];

    public static bool IsOrchestrator(string harness) => Normalize(harness) == Orchestrator;

    public static IReadOnlyList<string> CommandFor(string harness, bool fresh = false) =>
        Normalize(harness) switch
        {
            Nvim => [Nvim, "-c", NvimStartup],
            Orchestrator => fresh ? [Claude] : [Claude, ResumeArgument],
            _ => [Claude],
        };

    public static string Normalize(string harness) =>
        Known.Contains(harness.Trim()) ? harness.Trim() : Nvim;
}
