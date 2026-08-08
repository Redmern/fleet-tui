using Fleet.Shared.Keymap.Enums;
using Fleet.Ui.Models;
using Terminal.Gui.Input;

namespace Fleet.Ui;

public sealed class PrefixRecognizer(Keymap keymap)
{
    public bool Armed { get; private set; }

    public PrefixResult Feed(Key key)
    {
        if (!Armed)
        {
            if (key == keymap.Prefix)
            {
                Armed = true;
                return PrefixResult.Armed;
            }

            return PrefixResult.NotForFleet;
        }

        Armed = false;

        if (key == Key.Esc)
        {
            return PrefixResult.Cancelled;
        }

        if (key == keymap.Prefix)
        {
            Armed = true;
            return PrefixResult.Armed;
        }

        var action = keymap.ActionFor(key);

        return action == FleetAction.None
            ? PrefixResult.Cancelled
            : PrefixResult.For(action);
    }

    public void Disarm() => Armed = false;
}
