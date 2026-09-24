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
        + "local seenf='.fleet/instruction.seen' "
        + "if vim.fn.filereadable(seenf)==0 then pcall(vim.fn.mkdir,'.fleet','p') "
        + "pcall(vim.fn.writefile,{tostring(math.max(0,"
        + "vim.fn.getftime('.fleet/" + AgentInstructionFile + "')))},seenf) end "
        + "local function fleet_pump() "
        + "local m=vim.fn.getftime('.fleet/" + AgentInstructionFile + "') "
        + "if m<=0 then return end "
        + "local seen=vim.fn.filereadable(seenf)==1 "
        + "and tonumber(vim.fn.readfile(seenf)[1]) or 0 "
        + "if m<=(seen or 0) then return end "
        + "for _,b in ipairs(vim.api.nvim_list_bufs()) do "
        + "if vim.bo[b].buftype=='terminal' then local c=vim.b[b].terminal_job_id "
        + "if c then vim.fn.chansend(c, '" + AgentInstructionPrompt + "') "
        + "vim.defer_fn(function() vim.fn.chansend(c, '\\r') end, 400) "
        + "pcall(vim.fn.writefile,{tostring(m)},seenf) return end end end end "
        + "local fleet_timer=(vim.uv or vim.loop).new_timer() "
        + "fleet_timer:start(3000, 3000, vim.schedule_wrap(fleet_pump)) ";

    public const string NvimStartup =
        NvimBoot + "vim.schedule(function() vim.cmd('Neotree show') vim.cmd('stopinsert') end)";

    public const string NvimStartupWithClaude =
        NvimBoot
        + "vim.schedule(function() vim.cmd('Neotree show') vim.cmd('ClaudeCode') "
        + "vim.defer_fn(function() for _,w in ipairs(vim.api.nvim_list_wins()) do "
        + "local b=vim.api.nvim_win_get_buf(w) "
        + "if #vim.api.nvim_list_wins()>1 and vim.api.nvim_buf_get_name(b)=='' "
        + "and vim.bo[b].buftype=='' and not vim.bo[b].modified "
        + "and vim.api.nvim_buf_line_count(b)<=1 then "
        + "pcall(vim.api.nvim_win_close, w, true) end end vim.cmd('stopinsert') end, 150) end)";

    public static string NvimStartupClaudeOnly(string claudeArgs) =>
        NvimBoot
        + "vim.schedule(function() vim.cmd('ClaudeCode" + claudeArgs + "') "
        + "vim.defer_fn(function() local term "
        + "for _,w in ipairs(vim.api.nvim_list_wins()) do "
        + "if vim.bo[vim.api.nvim_win_get_buf(w)].buftype=='terminal' then term=w end end "
        + "if not term then return end "
        + "for _,w in ipairs(vim.api.nvim_list_wins()) do "
        + "if w~=term and vim.api.nvim_win_get_config(w).relative=='' then "
        + "pcall(vim.api.nvim_win_close, w, true) end end "
        + "vim.api.nvim_set_current_win(term) vim.cmd('startinsert') end, 150) end)";

    public static IReadOnlyList<string> OrchestratorCommand(bool resume) =>
        [Nvim, "-c", NvimStartupClaudeOnly(resume ? " " + ResumeArgument : string.Empty)];

    public static bool HostedInNvim(string harness) => CommandFor(harness)[0] == Nvim;

    public const string TellPrefix = ":FleetTell ";

    public const string AgentInstructionFile = "instruction.md";

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

    public static IReadOnlyDictionary<string, string> SpawnEnv(string harness) =>
        CommandFor(harness)[0] == Claude ? SessionPersistence : new Dictionary<string, string>();

    public static IReadOnlyList<string> All { get; } = [Nvim, Claude];

    public static IReadOnlyList<string> Known { get; } = [Nvim, Claude, Orchestrator];

    public static string Describe(string harness) => Normalize(harness);

    public const string BrowseStartup = "lua vim.schedule(function() vim.cmd('Neotree show') end)";

    public static IReadOnlyList<string> BrowseCommand { get; } = [Nvim, "-c", BrowseStartup];

    public const string TitledVerb = "titled";

    public const string TitleFlag = "--title";

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

    public static IReadOnlyList<string> CommandFor(string harness, bool withClaude = false) =>
        Normalize(harness) switch
        {
            Nvim => [Nvim, "-c", withClaude ? NvimStartupWithClaude : NvimStartup],
            Orchestrator => OrchestratorCommand(resume: false),
            _ => [Claude],
        };

    public static string Normalize(string harness) =>
        Known.Contains(harness.Trim()) ? harness.Trim() : Nvim;
}
