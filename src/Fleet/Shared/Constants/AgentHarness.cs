using Fleet.Shared.Keybinds;

namespace Fleet.Shared.Constants;

public static class AgentHarness
{
    public const string Claude = "claude";

    public const string Nvim = "nvim";

    public const string Orchestrator = "orchestrator";

    private const string NvimBoot =
        "lua vim.env.CLAUDE_CODE_FORCE_SESSION_PERSISTENCE='1' vim.env.CLAUDE_CODE_CHILD_SESSION=nil "
        + "vim.api.nvim_create_user_command('FleetTell', function(o) "
        + "for _,b in ipairs(vim.api.nvim_list_bufs()) do "
        + "if vim.bo[b].buftype=='terminal' then local c=vim.b[b].terminal_job_id "
        + "if c then vim.fn.chansend(c, o.args) "
        + "vim.defer_fn(function() vim.fn.chansend(c, '\\r') end, 400) end end end end, {nargs='+'}) "
        + "local seenf='.fleet/" + InstructionSeenFile + "' "
        + "local readyf='.fleet/" + ClaudeReadyFile + "' local fleet_boot=os.time() "
        + "if vim.fn.filereadable(seenf)==0 then pcall(vim.fn.mkdir,'.fleet','p') "
        + "pcall(vim.fn.writefile,{tostring(math.max(0,"
        + "vim.fn.getftime('.fleet/" + AgentInstructionFile + "')))},seenf) end "
        + "local function fleet_pump() "
        + "local m=vim.fn.getftime('.fleet/" + AgentInstructionFile + "') "
        + "if m<=0 then return end "
        + "local seen=vim.fn.filereadable(seenf)==1 "
        + "and tonumber(vim.fn.readfile(seenf)[1]) or 0 "
        + "if m<=(seen or 0) then return end "
        + "local age=os.time()-m "
        + "if age<" + InstructionSettleSeconds + " then return end "
        + "if vim.fn.getftime(readyf)<fleet_boot and age<" + ReadyFallbackSeconds + " then return end "
        + "for _,b in ipairs(vim.api.nvim_list_bufs()) do "
        + "if vim.bo[b].buftype=='terminal' then local c=vim.b[b].terminal_job_id "
        + "if c then vim.fn.chansend(c, '" + AgentInstructionPrompt + "') "
        + "vim.defer_fn(function() vim.fn.chansend(c, '\\r') end, 400) "
        + "pcall(vim.fn.writefile,{tostring(m)},seenf) return end end end end "
        + "local fleet_timer=(vim.uv or vim.loop).new_timer() "
        + "fleet_timer:start(3000, 3000, vim.schedule_wrap(fleet_pump)) ";

    public const string NvimStartup =
        NvimBoot + "vim.schedule(function() vim.cmd('Neotree show') vim.cmd('stopinsert') end)";

    public static string NvimStartupWithClaude(string claudeArgs) =>
        NvimBoot
        + "vim.schedule(function() vim.cmd('Neotree show') " + ClaudeCodeCall + claudeArgs + "') "
        + "vim.defer_fn(function() for _,w in ipairs(vim.api.nvim_list_wins()) do "
        + "local b=vim.api.nvim_win_get_buf(w) "
        + "if #vim.api.nvim_list_wins()>1 and vim.api.nvim_buf_get_name(b)=='' "
        + "and vim.bo[b].buftype=='' and not vim.bo[b].modified "
        + "and vim.api.nvim_buf_line_count(b)<=1 then "
        + "pcall(vim.api.nvim_win_close, w, true) end end "
        + "local tree,term for _,w in ipairs(vim.api.nvim_list_wins()) do "
        + "local b=vim.api.nvim_win_get_buf(w) "
        + "if vim.bo[b].filetype=='neo-tree' then tree=w "
        + "elseif vim.bo[b].buftype=='terminal' then term=w end end "
        + "if tree and term then vim.wo[term].winfixwidth=false "
        + "pcall(vim.api.nvim_win_set_width, tree, math.floor(vim.o.columns*0.25)) "
        + "vim.wo[tree].winfixwidth=true end "
        + "vim.cmd('stopinsert') end, 150) end)";

    public static string NvimStartupClaudeOnly(string claudeArgs) =>
        NvimBoot
        + "vim.schedule(function() " + ClaudeCodeCall + claudeArgs + "') "
        + "vim.defer_fn(function() local term "
        + "for _,w in ipairs(vim.api.nvim_list_wins()) do "
        + "if vim.bo[vim.api.nvim_win_get_buf(w)].buftype=='terminal' then term=w end end "
        + "if not term then return end "
        + "for _,w in ipairs(vim.api.nvim_list_wins()) do "
        + "if w~=term and vim.api.nvim_win_get_config(w).relative=='' then "
        + "pcall(vim.api.nvim_win_close, w, true) end end "
        + "for _,b in ipairs(vim.api.nvim_list_bufs()) do "
        + "if b~=vim.api.nvim_win_get_buf(term) and vim.api.nvim_buf_get_name(b)=='' "
        + "and vim.bo[b].buftype=='' and not vim.bo[b].modified "
        + "and vim.api.nvim_buf_line_count(b)<=1 then "
        + "pcall(vim.api.nvim_buf_delete, b, {force=true}) end end "
        + "local tb=vim.api.nvim_win_get_buf(term) "
        + "vim.defer_fn(function() if not vim.api.nvim_buf_is_valid(tb) then return end "
        + "for _,f in ipairs(" + NvimFocusMaps.LuaTable(KeybindDefaults.Set) + ") do local k,d=f[2],f[3] "
        + "vim.keymap.set(f[4],f[1],function() "
        + "if vim.fn.winnr(k)~=vim.fn.winnr() then vim.cmd('stopinsert') vim.cmd('wincmd '..k) "
        + "else vim.fn.jobstart({vim.env.WEZTERM_EXECUTABLE or 'wezterm','cli','activate-pane-direction',d}) end "
        + "end,{buffer=tb}) end end, 400) "
        + "vim.api.nvim_set_current_win(term) vim.cmd('startinsert') end, 150) end)";

    private const string ClaudeCodeCall = "vim.cmd('ClaudeCode";

    public static IReadOnlyList<string> OrchestratorCommand(
        bool resume, bool inNvim = true, ClaudeLaunch? launch = null) =>
        inNvim
            ? [Nvim, "-c", NvimStartupClaudeOnly(NvimArguments(ClaudeArguments(resume, launch)))]
            : [Claude, .. ClaudeArguments(resume, launch)];

    public static IReadOnlyList<string> Resumed(IReadOnlyList<string> command) => command switch
    {
        [Claude, ..] when !command.Contains(ResumeArgument) => [Claude, ResumeArgument, .. command.Skip(1)],
        [Nvim, "-c", var startup]
            when startup.Contains(ClaudeCodeCall, StringComparison.Ordinal)
                && !startup.Contains(ClaudeCodeCall + " " + ResumeArgument, StringComparison.Ordinal) =>
            [Nvim, "-c", startup.Replace(ClaudeCodeCall, ClaudeCodeCall + " " + ResumeArgument, StringComparison.Ordinal)],
        _ => command,
    };

    private static IReadOnlyList<string> ClaudeArguments(bool resume, ClaudeLaunch? launch) =>
        [.. resume ? [ResumeArgument] : Array.Empty<string>(), .. launch?.Arguments ?? []];

    private static string NvimArguments(IReadOnlyList<string> arguments) =>
        string.Concat(arguments.Select(a => " " + a));

    public static bool HostedInNvim(string harness) => CommandFor(harness)[0] == Nvim;

    public const string TellPrefix = ":FleetTell ";

    public const string AgentInstructionFile = "instruction.md";

    public const string InstructionSeenFile = "instruction.seen";

    public const string ClaudeReadyFile = "claude.ready";

    public const string InstructionSettleSeconds = "2";

    public const string ReadyFallbackSeconds = "60";

    public const string AgentInstructionPrompt =
        "Read .fleet/instruction.md in this folder and do what it says.";

    public const string OrchestratorKickoff =
        "Read CLAUDE.md and TASK.md in your working directory, then begin.";

    public const string ResumeArgument = "--continue";

    public static IReadOnlyDictionary<string, string> SessionPersistence { get; } =
        new Dictionary<string, string>
        {
            ["CLAUDE_CODE_FORCE_SESSION_PERSISTENCE"] = "1",
            ["CLAUDE_CODE_CHILD_SESSION"] = string.Empty,
        };

    public static IReadOnlyDictionary<string, string> SpawnEnv(string harness, bool orchestratorInNvim = true) =>
        CommandFor(harness, orchestratorInNvim: orchestratorInNvim)[0] == Claude
            ? SessionPersistence
            : new Dictionary<string, string>();

    public const string NvimAppNameVariable = "NVIM_APPNAME";

    public const string FleetNvimAppName = "fleet-nvim";

    public static bool LaunchesNvim(IReadOnlyList<string> command) =>
        command.Count > 0
        && (command[0] == Nvim || (TitledPaneTitle(command) is not null && command.Count > 5 && command[5] == Nvim));

    public static IReadOnlyDictionary<string, string> WithFleetNvimConfig(
        IReadOnlyList<string> command, IReadOnlyDictionary<string, string> env) =>
        !LaunchesNvim(command) || env.ContainsKey(NvimAppNameVariable)
            ? env
            : new Dictionary<string, string>(env) { [NvimAppNameVariable] = FleetNvimAppName };

    public static IReadOnlyList<string> All { get; } = [Nvim, Claude];

    public static IReadOnlyList<string> Known { get; } = [Nvim, Claude, Orchestrator];

    public static string Describe(string harness) => Normalize(harness);

    public const string BrowseStartup = "lua vim.schedule(function() vim.cmd('Neotree show') end)";

    public static IReadOnlyList<string> BrowseCommand { get; } = [Nvim, "-c", BrowseStartup];

    public const string TitledVerb = "titled";

    public const string TitleFlag = "--title";

    public const string WithEnvVerb = "with-env";

    public static IReadOnlyList<string> BrowseCommandFor(string paneTitle) =>
        [Environment.ProcessPath ?? "fleet", TitledVerb, TitleFlag, paneTitle, "--", .. BrowseCommand];

    public static string? TitledPaneTitle(IReadOnlyList<string> command) =>
        command.Count >= 5
        && command[1] == TitledVerb
        && command[2] == TitleFlag
        && command[4] == "--"
            ? command[3]
            : null;

    public static bool IsOrchestrator(string harness) => Normalize(harness) == Orchestrator;

    public static IReadOnlyList<string> CommandFor(
        string harness, bool withClaude = false, bool orchestratorInNvim = true, ClaudeLaunch? launch = null) =>
        Normalize(harness) switch
        {
            Nvim => [Nvim, "-c", withClaude ? NvimStartupWithClaude(NvimArguments(ClaudeArguments(false, launch))) : NvimStartup],
            Orchestrator => OrchestratorCommand(resume: false, inNvim: orchestratorInNvim, launch),
            _ => [Claude, .. ClaudeArguments(false, launch)],
        };

    public static string Normalize(string harness) =>
        Known.Contains(harness.Trim()) ? harness.Trim() : Nvim;
}
