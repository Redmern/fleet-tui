using Fleet.Shared.Keymap.Enums;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace Fleet.Ui;

public static class FleetKeys
{
    public static readonly Key Cancel = Key.Esc;

    public static void ApplyMotions(View view, Keymap keymap)
    {
        Bind(view, keymap.KeyFor(FleetAction.MoveDown), Command.Down);
        Bind(view, keymap.KeyFor(FleetAction.MoveUp), Command.Up);
        Bind(view, keymap.KeyFor(FleetAction.MoveFirst), Command.Start);
        Bind(view, keymap.KeyFor(FleetAction.MoveLast), Command.End);
        Bind(view, keymap.KeyFor(FleetAction.PageDown), Command.PageDown);
        Bind(view, keymap.KeyFor(FleetAction.PageUp), Command.PageUp);
    }

    public static void ApplyOpen(View view, Keymap keymap)
        => Bind(view, keymap.KeyFor(FleetAction.OpenProject), Command.Accept);

    private static void Bind(View view, Key key, params Command[] commands)
    {
        if (key.IsValid)
        {
            view.KeyBindings.ReplaceCommands(key, commands);
        }
    }
}
