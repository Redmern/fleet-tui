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

    public static int Count => Tools.Count + 5;

    public static bool IsTriggerRow(int index) => index == 0;

    public static bool IsCommitRow(int index) => index == Tools.Count + 1;

    public static bool IsPushRow(int index) => index == Tools.Count + 2;

    public static bool IsMergeRow(int index) => index == Tools.Count + 3;

    public static bool IsStatusHooksRow(int index) => index == Tools.Count + 4;

    public static HarnessTool ToolAt(int index) =>
        index >= 1 && index <= Tools.Count ? Tools[index - 1] : HarnessTool.None;

    public static string Title(string project) => $"{project} — harness permissions";

    public const string NameHeading = "setting";

    public const string ValueHeading = "value";

    public const string ChannelHeading = "ask via";

    public const string Separator = " │ ";

    private const int ValueWidth = 6;

    public static FleetRow Header(SettingsConfig config) =>
        new(
        [
            FleetSpan.Muted(NameHeading.PadRight(LabelWidth())),
            new FleetSpan(Separator, FleetTones.Edge),
            FleetSpan.Muted(ValueHeading.PadRight(ValueWidthFor(config))),
            new FleetSpan(Separator, FleetTones.Edge),
            FleetSpan.Muted(ChannelHeading),
        ]);

    public static IReadOnlyList<FleetRow> For(SettingsConfig config)
    {
        var labelWidth = LabelWidth();
        var valueWidth = ValueWidthFor(config);

        List<FleetRow> rows =
        [
            Cells(TriggerLabel, labelWidth, new FleetSpan(config.Trigger.PadRight(valueWidth), FleetTones.Key), "—"),
        ];

        rows.AddRange(Tools.Select(t => Row(t, config.RuleFor(t), labelWidth, valueWidth)));
        rows.Add(GateRow(SettingsDefaults.CommitLabel, config.Commit, labelWidth, valueWidth));
        rows.Add(GateRow(SettingsDefaults.PushLabel, config.Push, labelWidth, valueWidth));
        rows.Add(GateRow(SettingsDefaults.MergeLabel, config.Merge, labelWidth, valueWidth));
        rows.Add(SwitchRow(SettingsDefaults.StatusHooksLabel, config.StatusHooks, labelWidth, valueWidth));

        return rows;
    }

    private static int LabelWidth() =>
        new[]
            {
                NameHeading.Length,
                TriggerLabel.Length,
                SettingsDefaults.CommitLabel.Length,
                SettingsDefaults.PushLabel.Length,
                SettingsDefaults.MergeLabel.Length,
                SettingsDefaults.StatusHooksLabel.Length,
                Tools.Max(t => SettingsDefaults.Describe(t).Length),
            }
            .Max();

    private static int ValueWidthFor(SettingsConfig config) =>
        Math.Max(ValueWidth, config.Trigger.Length);

    private static FleetRow Cells(string label, int labelWidth, FleetSpan value, string channel) =>
        new(
        [
            FleetSpan.Plain(label.PadRight(labelWidth)),
            new FleetSpan(Separator, FleetTones.Edge),
            value,
            new FleetSpan(Separator, FleetTones.Edge),
            FleetSpan.Muted(channel),
        ]);
    public static IReadOnlyList<PickerEntry> PolicyEntries() =>
    [
        new("allow", "let the harness do it", "a"),
        new("ask", "ask before doing it", "s"),
        new("forbid", "never", "f"),
    ];

    public static IReadOnlyList<PickerEntry> GatePolicyEntries() =>
    [
        new("auto", "do it without asking", "a"),
        new("ask", "ask for permission", "s"),
        new("no", "never", "f"),
    ];

    private static FleetRow GateRow(string label, ActionPolicy policy, int labelWidth, int valueWidth) =>
        Cells(label, labelWidth, new FleetSpan(GateWord(policy).PadRight(valueWidth), ToneFor(policy)), "—");

    private static FleetRow SwitchRow(string label, bool on, int labelWidth, int valueWidth) =>
        Cells(
            label,
            labelWidth,
            new FleetSpan((on ? "on" : "off").PadRight(valueWidth), on ? FleetTones.Good : FleetTones.Muted),
            "—");

    private static string GateWord(ActionPolicy policy) => policy switch
    {
        ActionPolicy.Allow => "auto",
        ActionPolicy.Ask => "ask",
        _ => "no",
    };

    public static IReadOnlyList<PickerEntry> ChannelEntries() =>
    [
        new("both", "fleet and claude ask", "b"),
        new("fleetDialog", "only the fleet dashboard asks", "d"),
        new("claudePermission", "only claude asks", "c"),
    ];

    private static FleetRow Row(HarnessTool tool, ToolRule rule, int labelWidth, int valueWidth)
    {
        var policy = rule.Policy.ToString().ToLowerInvariant();
        var channel = rule.Policy == ActionPolicy.Ask
            ? rule.Channel.ToString().ToLowerInvariant()
            : "—";

        return Cells(
            SettingsDefaults.Describe(tool),
            labelWidth,
            new FleetSpan(policy.PadRight(valueWidth), ToneFor(rule.Policy)),
            channel);
    }

    private static string ToneFor(ActionPolicy policy) => policy switch
    {
        ActionPolicy.Allow => FleetTones.Good,
        ActionPolicy.Ask => FleetTones.Warn,
        _ => FleetTones.Bad,
    };
}
