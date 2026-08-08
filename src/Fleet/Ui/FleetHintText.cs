using Fleet.Shared.Keymap.Enums;

namespace Fleet.Ui;

public static class FleetHintText
{
    public static string Motions(Keymap keymap) =>
        string.Join(
            "   ",
            $"{keymap.DisplayFor(FleetAction.MoveDown)}/{keymap.DisplayFor(FleetAction.MoveUp)} move",
            $"{keymap.DisplayFor(FleetAction.MoveFirst)}/{keymap.DisplayFor(FleetAction.MoveLast)} first/last");

    public static string Picker(Keymap keymap) =>
        string.Join(
            "   ",
            Motions(keymap),
            $"{keymap.DisplayFor(FleetAction.OpenProject)}/enter open",
            $"{keymap.DisplayFor(FleetAction.NewProject)} new",
            $"{keymap.DisplayFor(FleetAction.Close)}/esc quit",
            Menu(keymap));

    public static string Dashboard(Keymap keymap) =>
        string.Join(
            "   ",
            Motions(keymap),
            "tab pane",
            $"{keymap.DisplayFor(FleetAction.AddRepository)} add repo",
            $"{keymap.DisplayFor(FleetAction.Refresh)} refresh",
            $"{keymap.DisplayFor(FleetAction.Close)} close",
            Menu(keymap));

    public static string Menu(Keymap keymap) =>
        $"{keymap.PrefixDisplay} {keymap.DisplayFor(FleetAction.OpenMenu)} menu";
}
