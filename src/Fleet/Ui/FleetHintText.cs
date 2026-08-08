using Fleet.Shared.Keymap.Enums;

namespace Fleet.Ui;

public static class FleetHintText
{
    public static string Motions(Keymap keymap) =>
        string.Join(
            "   ",
            $"{keymap.TextFor(FleetAction.MoveDown)}/{keymap.TextFor(FleetAction.MoveUp)} move",
            $"{keymap.TextFor(FleetAction.MoveFirst)}/{keymap.TextFor(FleetAction.MoveLast)} first/last");

    public static string Picker(Keymap keymap) =>
        string.Join(
            "   ",
            Motions(keymap),
            $"{keymap.TextFor(FleetAction.OpenProject)}/enter open",
            $"{keymap.TextFor(FleetAction.NewProject)} new",
            $"{keymap.TextFor(FleetAction.Close)}/esc quit",
            Menu(keymap));

    public static string Dashboard(Keymap keymap) =>
        string.Join(
            "   ",
            Motions(keymap),
            "tab pane",
            $"{keymap.TextFor(FleetAction.AddRepository)} add repo",
            $"{keymap.TextFor(FleetAction.Refresh)} refresh",
            $"{keymap.TextFor(FleetAction.Close)} close",
            Menu(keymap));

    public static string Menu(Keymap keymap) =>
        $"{keymap.PrefixText} {keymap.TextFor(FleetAction.OpenMenu)} menu";
}
