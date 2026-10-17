using Fleet.Shared.Settings.Enums;
using Fleet.Shared.Settings.Models;

namespace Fleet.Features.Menu.EditSettings;

public static class SettingsTabs
{
    public const int All = 0;

    public const int FleetDialog = 1;

    public const int Claude = 2;

    public static IReadOnlyList<string> Titles { get; } = ["All", "Fleet dialog", "Claude"];

    public static int Next(int tab) => (tab + 1) % Titles.Count;

    public static int Previous(int tab) => (tab - 1 + Titles.Count) % Titles.Count;

    public static IReadOnlyList<int> Visible(int tab, SettingsConfig config) =>
        [.. Enumerable.Range(0, SettingsRows.Count).Where(index => Shows(tab, config, index))];

    private static bool Shows(int tab, SettingsConfig config, int index)
    {
        if (tab == All)
        {
            return true;
        }

        var tool = SettingsRows.ToolAt(index);

        if (tool == HarnessTool.None)
        {
            return false;
        }

        var rule = config.RuleFor(tool);

        if (rule.Policy != ActionPolicy.Ask)
        {
            return false;
        }

        return tab == FleetDialog
            ? rule.Channel is AskChannel.FleetDialog or AskChannel.Both
            : rule.Channel is AskChannel.ClaudePermission or AskChannel.Both;
    }
}
