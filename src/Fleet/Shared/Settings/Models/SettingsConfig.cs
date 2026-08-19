using Fleet.Shared.Settings.Enums;

namespace Fleet.Shared.Settings.Models;

public sealed record SettingsConfig(
    string Trigger,
    IReadOnlyDictionary<HarnessTool, ToolRule> Rules,
    ActionPolicy Commit,
    ActionPolicy Push)
{
    public static SettingsConfig Default => new(
        SettingsDefaults.Trigger,
        SettingsDefaults.Rules.ToDictionary(r => r.Key, r => r.Value),
        SettingsDefaults.Commit,
        SettingsDefaults.Push);

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
            Push);
    }

    public string Signature =>
        string.Join(
            '|',
            Rules
                .OrderBy(r => r.Key)
                .Select(r => $"{HarnessToolIds.For(r.Key)}={r.Value.Policy}:{r.Value.Channel}")
                .Prepend($"trigger={Trigger}")
                .Append($"commit={Commit}")
                .Append($"push={Push}"));
}
