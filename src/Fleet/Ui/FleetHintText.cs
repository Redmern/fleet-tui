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

    public static string Agents(Keymap keymap) =>
        string.Join(
            "   ",
            $"{keymap.DisplayFor(FleetAction.NewAgent)} new",
            "enter open",
            $"{keymap.DisplayFor(FleetAction.ChangeHarness)} opens",
            $"{keymap.DisplayFor(FleetAction.ToggleHidden)} hide",
            $"{keymap.DisplayFor(FleetAction.RemoveAgent)} manage",
            Menu(keymap));

    public static string Repositories(Keymap keymap) =>
        string.Join(
            "   ",
            $"{keymap.DisplayFor(FleetAction.AddRepository)} add",
            $"{keymap.DisplayFor(FleetAction.RemoveRepository)} remove",
            $"{keymap.DisplayFor(FleetAction.Refresh)} refresh",
            Menu(keymap));

    public static string Menu(Keymap keymap) =>
        $"{keymap.PrefixDisplay} {keymap.DisplayFor(FleetAction.OpenMenu)} menu";
}
