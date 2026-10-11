using Fleet.Features.Todos.ShowBoard.Enums;
using Fleet.Shared.Keymap.Enums;
using Fleet.Ui;
using Terminal.Gui.Input;

namespace Fleet.Features.Todos.ShowBoard;

public static class BoardKeys
{
    public static readonly Key Effort = Key.E;

    public static readonly Key Close = Key.X;

    public static BoardCommand For(Key key, Keymap keymap)
    {
        if (key == Effort)
        {
            return BoardCommand.Effort;
        }

        if (key == Close)
        {
            return BoardCommand.Close;
        }

        if (key == keymap.KeyFor(FleetAction.Refresh))
        {
            return BoardCommand.Refresh;
        }

        return key == keymap.KeyFor(FleetAction.Close) ? BoardCommand.Quit : BoardCommand.None;
    }

    public static IReadOnlyList<(string Key, string Label, BoardCommand Command)> Hints(Keymap keymap) =>
    [
        ("enter", "open", BoardCommand.Open),
        ("e", "effort", BoardCommand.Effort),
        ("x", "close", BoardCommand.Close),
        (keymap.DisplayFor(FleetAction.Refresh), "refresh", BoardCommand.Refresh),
        (keymap.DisplayFor(FleetAction.Close), "quit", BoardCommand.Quit),
    ];
}
