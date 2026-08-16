namespace Fleet.Shared.Constants;

public static class AgentHarness
{
    public const string Claude = "claude";

    public const string Nvim = "nvim";

    public const string Orchestrator = "orchestrator";

    public const string NvimStartup =
        "lua vim.env.CLAUDE_CODE_FORCE_SESSION_PERSISTENCE='1' vim.env.CLAUDE_CODE_CHILD_SESSION=nil "
        + "vim.api.nvim_create_user_command('FleetTell', function(o) "
        + "for _,b in ipairs(vim.api.nvim_list_bufs()) do "
        + "if vim.bo[b].buftype=='terminal' then local c=vim.b[b].terminal_job_id "
        + "if c then vim.fn.chansend(c, o.args..'\\r') end end end end, {nargs='+'}) "
        + "vim.schedule(function() vim.cmd('Neotree show') vim.cmd('ClaudeCode') end)";

    public const string TellPrefix = ":FleetTell ";

    public const string OrchestratorKickoff =
        "Read CLAUDE.md and TASK.md in this folder, then begin.";

    public const string ResumeArgument = "--continue";

    public static IReadOnlyDictionary<string, string> SessionPersistence { get; } =
        new Dictionary<string, string>
        {
            ["CLAUDE_CODE_FORCE_SESSION_PERSISTENCE"] = "1",
            ["CLAUDE_CODE_CHILD_SESSION"] = string.Empty,
        };

    public static IReadOnlyDictionary<string, string> SpawnEnv(string harness) =>
        CommandFor(harness)[0] == Claude ? SessionPersistence : new Dictionary<string, string>();

    public static IReadOnlyList<string> All { get; } = [Nvim, Claude];

    public static IReadOnlyList<string> Known { get; } = [Nvim, Claude, Orchestrator];

    public static string Describe(string harness) => Normalize(harness);

    public const string BrowseStartup = "lua vim.schedule(function() vim.cmd('Neotree show') end)";

    public static IReadOnlyList<string> BrowseCommand { get; } = [Nvim, "-c", BrowseStartup];

    public static bool IsOrchestrator(string harness) => Normalize(harness) == Orchestrator;

    public static IReadOnlyList<string> CommandFor(string harness) =>
        Normalize(harness) switch
        {
            Nvim => [Nvim, "-c", NvimStartup],
            Orchestrator => [Claude],
            _ => [Claude],
        };

    public static string Normalize(string harness) =>
        Known.Contains(harness.Trim()) ? harness.Trim() : Nvim;
}
