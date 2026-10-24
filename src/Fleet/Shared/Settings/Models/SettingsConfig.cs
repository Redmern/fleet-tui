using Fleet.Shared.Results;
using Fleet.Shared.Settings.Enums;

namespace Fleet.Shared.Settings.Models;

public sealed record SettingsConfig(
    string Trigger,
    IReadOnlyDictionary<HarnessTool, ToolRule> Rules,
    ActionPolicy Commit,
    ActionPolicy Push,
    AidlcSettings Aidlc,
    bool MainOrchestratorInNvim = SettingsDefaults.MainOrchestratorInNvim,
    bool SubOrchestratorsInNvim = SettingsDefaults.SubOrchestratorsInNvim,
    bool AutoClose = SettingsDefaults.AutoClose,
    int AutoCloseMinutes = SettingsDefaults.AutoCloseMinutes,
    ActionPolicy Merge = SettingsDefaults.Merge)
{
    public static SettingsConfig Default => new(
        SettingsDefaults.Trigger,
        SettingsDefaults.Rules.ToDictionary(r => r.Key, r => r.Value),
        SettingsDefaults.Commit,
        SettingsDefaults.Push,
        SettingsDefaults.Aidlc,
        SettingsDefaults.MainOrchestratorInNvim,
        SettingsDefaults.SubOrchestratorsInNvim,
        Merge: SettingsDefaults.Merge);

    public string TodoTrigger { get; init; } = SettingsDefaults.TodoTrigger;

    public bool StatusHooks { get; init; } = SettingsDefaults.StatusHooks;

    public bool SubagentGuidance { get; init; } = SettingsDefaults.SubagentGuidance;

    public bool ShowMenuKeys { get; init; } = SettingsDefaults.ShowMenuKeys;

    public bool Iso { get; init; } = SettingsDefaults.Iso;

    public NvimConfig Nvim { get; init; } = SettingsDefaults.Nvim;
    public ButtonHints ButtonHints { get; init; } = SettingsDefaults.ButtonHints;

    public RoleModels Models { get; init; } = SettingsDefaults.Models;

    public JiraSettings? Jira { get; init; }

    public string LastRepo { get; init; } = string.Empty;

    public string LastHarness { get; init; } = string.Empty;

    public Editor? Editor { get; init; }

    public Editor? DefaultEditor { get; init; }

    public Editor? InstalledEditor { get; init; } = Enums.Editor.Nvim;

    public Editor ResolvedEditor => EditorChoice.Resolve(
        Editor, DefaultEditor, InstalledEditor == Enums.Editor.Nvim, InstalledEditor == Enums.Editor.Vscode);

    public bool HostsInNvim => ResolvedEditor == Enums.Editor.Nvim;

    public bool MainOrchestratorHostedInNvim => MainOrchestratorInNvim && HostsInNvim;

    public bool SubOrchestratorsHostedInNvim => SubOrchestratorsInNvim && HostsInNvim;

    public string DefaultAgentHarness => HostsInNvim ? Constants.AgentHarness.Nvim : Constants.AgentHarness.Claude;

    public ToolRule RuleFor(HarnessTool tool) =>
        Rules.TryGetValue(tool, out var rule) ? rule : SettingsDefaults.RuleFor(tool);

    public SettingsConfig With(HarnessTool tool, ActionPolicy policy)
    {
        var rules = Rules.ToDictionary(r => r.Key, r => r.Value);
        rules[tool] = RuleFor(tool) with { Policy = policy };
        return this with { Rules = rules };
    }

    public SettingsConfig With(HarnessTool tool, AskChannel channel)
    {
        var rules = Rules.ToDictionary(r => r.Key, r => r.Value);
        rules[tool] = RuleFor(tool) with { Channel = channel };
        return this with { Rules = rules };
    }

    public SettingsConfig WithTrigger(string trigger) => this with { Trigger = trigger };

    public string? DispatchTriggerRefusal(string trigger) =>
        TodoTrigger.Length > 0 && string.Equals(trigger, TodoTrigger, StringComparison.Ordinal)
            ? $"'{trigger}' is the todo trigger; the dispatch trigger must differ."
            : null;

    public Result<SettingsConfig> WithTodoTrigger(string trigger)
    {
        if (!DispatchTrigger.IsValid(trigger))
        {
            return Result<SettingsConfig>.Fail("The todo trigger must be one symbol, not a letter, digit or space.");
        }

        return string.Equals(trigger, Trigger, StringComparison.Ordinal)
            ? Result<SettingsConfig>.Fail($"The todo trigger cannot be the dispatch trigger '{Trigger}'.")
            : Result<SettingsConfig>.Ok(this with { TodoTrigger = trigger });
    }

    public SettingsConfig WithCommit(ActionPolicy policy) => this with { Commit = policy };

    public SettingsConfig WithPush(ActionPolicy policy) => this with { Push = policy };

    public SettingsConfig WithMerge(ActionPolicy policy) => this with { Merge = policy };

    public SettingsConfig WithAidlcMode(AidlcMode mode) => this with { Aidlc = Aidlc with { Mode = mode } };

    public SettingsConfig WithAidlc(AidlcSettings aidlc) => this with { Aidlc = aidlc };

    public SettingsConfig WithMainOrchestratorInNvim(bool inNvim) => this with { MainOrchestratorInNvim = inNvim };

    public SettingsConfig WithSubOrchestratorsInNvim(bool inNvim) => this with { SubOrchestratorsInNvim = inNvim };

    public SettingsConfig WithAutoClose(bool on, int minutes) =>
        this with { AutoClose = on, AutoCloseMinutes = minutes };

    public SettingsConfig WithStatusHooks(bool on) => this with { StatusHooks = on };

    public SettingsConfig WithSubagentGuidance(bool on) => this with { SubagentGuidance = on };

    public SettingsConfig WithShowMenuKeys(bool on) => this with { ShowMenuKeys = on };

    public SettingsConfig WithIso(bool on) => this with { Iso = on };

    public SettingsConfig WithNvim(NvimConfig nvim) => this with { Nvim = nvim };
    public SettingsConfig WithButtonHints(ButtonHints hints) => this with { ButtonHints = hints };

    public SettingsConfig WithModels(RoleModels models) => this with { Models = models };

    public SettingsConfig WithEditor(Editor? editor) => this with { Editor = editor };

    public SettingsConfig WithLastNewAgent(string repository, string harness) =>
        this with { LastRepo = repository, LastHarness = harness };

    public SettingsConfig MergedOverDefaults()
    {
        var rules = SettingsDefaults.Rules.ToDictionary(r => r.Key, r => r.Value);

        foreach (var (tool, rule) in Rules)
        {
            if (rules.ContainsKey(tool))
            {
                rules[tool] = rule;
            }
        }

        var trigger = DispatchTrigger.IsValid(Trigger) ? Trigger : SettingsDefaults.Trigger;
        var todoTrigger = DispatchTrigger.IsValid(TodoTrigger) ? TodoTrigger : SettingsDefaults.TodoTrigger;

        return new SettingsConfig(
            trigger,
            rules,
            Commit,
            Push,
            Aidlc,
            MainOrchestratorInNvim,
            SubOrchestratorsInNvim,
            AutoClose,
            AutoCloseMinutes > 0 ? AutoCloseMinutes : SettingsDefaults.AutoCloseMinutes,
            Merge)
        {
            TodoTrigger = todoTrigger == trigger ? string.Empty : todoTrigger,
            StatusHooks = StatusHooks,
            SubagentGuidance = SubagentGuidance,
            ShowMenuKeys = ShowMenuKeys,
            Iso = Iso,
            Nvim = Nvim,
            ButtonHints = ButtonHints,
            Models = new RoleModels(Models.Main.Normalized, Models.Sub.Normalized, Models.Agent.Normalized),
            Jira = Jira,
            LastRepo = LastRepo,
            LastHarness = LastHarness,
            Editor = Editor,
            DefaultEditor = DefaultEditor,
            InstalledEditor = InstalledEditor,
        };
    }

    public string Signature =>
        string.Join(
            '|',
            Rules
                .OrderBy(r => r.Key)
                .Select(r => $"{HarnessToolIds.For(r.Key)}={r.Value.Policy}:{r.Value.Channel}")
                .Prepend($"trigger={Trigger}")
                .Append($"commit={Commit}")
                .Append($"push={Push}")
                .Append($"merge={Merge}")
                .Append(Aidlc.Signature)
                .Append($"main-nvim={MainOrchestratorInNvim}")
                .Append($"sub-nvim={SubOrchestratorsInNvim}")
                .Append($"autoclose={AutoClose}:{AutoCloseMinutes}")
                .Append($"hooks={StatusHooks}")
                .Append($"subagents={SubagentGuidance}")
                .Append($"menu-keys={ShowMenuKeys}")
                .Append($"iso={Iso}")
                .Append($"nvim-config={Nvim}")
                .Append($"button-hints={ButtonHints}")
                .Append($"editor={EditorChoice.Word(Editor)}")
                .Append(Models.Signature));
}
