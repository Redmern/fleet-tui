using Fleet.Shared.Settings.Enums;
using Fleet.Shared.Settings.Models;

namespace Fleet.Shared.Settings;

public static class SettingsDiff
{
    public static string TriggerAgainstDefault(string trigger) =>
        string.Equals(trigger, SettingsDefaults.Trigger, StringComparison.Ordinal)
            ? string.Empty
            : trigger;

    public static IReadOnlyDictionary<HarnessTool, ToolRule> AgainstDefaults(
        IReadOnlyDictionary<HarnessTool, ToolRule> rules)
    {
        var changed = new Dictionary<HarnessTool, ToolRule>();

        foreach (var (tool, rule) in rules)
        {
            if (!SettingsDefaults.Rules.TryGetValue(tool, out var shipped))
            {
                continue;
            }

            if (shipped != rule)
            {
                changed[tool] = rule;
            }
        }

        return changed;
    }
}
