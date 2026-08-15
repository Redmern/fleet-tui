using Fleet.Shared.Settings;
using Fleet.Shared.Settings.Enums;
using Fleet.Shared.Settings.Models;
using Fleet.Ui.Constants;
using Fleet.Ui.Models;

namespace Fleet.Features.Menu.EditSettings;

public static class SettingsRows
{
    public const string TriggerLabel = "dispatch trigger";

    public static IReadOnlyList<HarnessTool> Tools => SettingsDefaults.Configurable;

    public static int Count => Tools.Count + 1;

    public static bool IsTriggerRow(int index) => index == 0;

    public static HarnessTool ToolAt(int index) =>
        index >= 1 && index <= Tools.Count ? Tools[index - 1] : HarnessTool.None;

    public static string Title(string project) => $"{project} — harness permissions";

    public static IReadOnlyList<FleetRow> For(SettingsConfig config)
    {
        var labelWidth = Math.Max(
            TriggerLabel.Length,
            Tools.Max(t => SettingsDefaults.Describe(t).Length));

        List<FleetRow> rows =
        [
            new FleetRow(
                [FleetSpan.Plain(TriggerLabel.PadRight(labelWidth))],
                [new FleetSpan(config.Trigger, FleetTones.Key)]),
        ];

        rows.AddRange(Tools.Select(t => Row(t, config.RuleFor(t), labelWidth)));

        return rows;
    }

    public static IReadOnlyList<PickerEntry> PolicyEntries() =>
    [
        new("allow", "let the harness do it", "a"),
        new("ask", "ask before doing it", "s"),
        new("forbid", "never", "f"),
    ];

    public static IReadOnlyList<PickerEntry> ChannelEntries() =>
    [
        new("both", "fleet and claude ask", "b"),
        new("fleetDialog", "only the fleet dashboard asks", "d"),
        new("claudePermission", "only claude asks", "c"),
    ];

    private static FleetRow Row(HarnessTool tool, ToolRule rule, int labelWidth)
    {
        var policy = rule.Policy.ToString().ToLowerInvariant();
        var channel = rule.Policy == ActionPolicy.Ask
            ? rule.Channel.ToString().ToLowerInvariant()
            : "—";

        return new FleetRow(
            [FleetSpan.Plain(SettingsDefaults.Describe(tool).PadRight(labelWidth))],
            [
                new FleetSpan(policy.PadRight(6) + "  ", ToneFor(rule.Policy)),
                FleetSpan.Muted(channel),
            ]);
    }

    private static string ToneFor(ActionPolicy policy) => policy switch
    {
        ActionPolicy.Allow => FleetTones.Good,
        ActionPolicy.Ask => FleetTones.Warn,
        _ => FleetTones.Bad,
    };
}
