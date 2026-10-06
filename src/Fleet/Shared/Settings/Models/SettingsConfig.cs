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

    public bool StatusHooks { get; init; } = SettingsDefaults.StatusHooks;

    public RoleModels Models { get; init; } = SettingsDefaults.Models;

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

    public SettingsConfig WithModels(RoleModels models) => this with { Models = models };

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

        return new SettingsConfig(
            DispatchTrigger.IsValid(Trigger) ? Trigger : SettingsDefaults.Trigger,
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
            StatusHooks = StatusHooks,
            Models = new RoleModels(Models.Main.Normalized, Models.Sub.Normalized, Models.Agent.Normalized),
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
                .Append(Models.Signature));
}
