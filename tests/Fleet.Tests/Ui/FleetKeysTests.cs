using Fleet.Shared.Keymap.Enums;
using Fleet.Ui;
using Terminal.Gui.Input;
using Terminal.Gui.Views;

namespace Fleet.Tests.Ui;

public class FleetKeysTests
{
    private static Keymap Map => Keymap.Default;

    [Fact]
    public void ApplyMotions_does_not_throw_on_keys_the_widget_already_binds()
    {
        var list = new ListView();

        FleetKeys.ApplyMotions(list, Map);
        FleetKeys.ApplyOpen(list, Map);
    }

    [Fact]
    public void ApplyMotions_is_idempotent()
    {
        var list = new ListView();

        FleetKeys.ApplyMotions(list, Map);
        FleetKeys.ApplyMotions(list, Map);
    }

    [Theory]
    [InlineData(FleetAction.MoveDown, Command.Down)]
    [InlineData(FleetAction.MoveUp, Command.Up)]
    [InlineData(FleetAction.MoveFirst, Command.Start)]
    [InlineData(FleetAction.MoveLast, Command.End)]
    [InlineData(FleetAction.PageDown, Command.PageDown)]
    [InlineData(FleetAction.PageUp, Command.PageUp)]
    public void Each_motion_maps_to_its_command(FleetAction action, Command expected)
    {
        var list = new ListView();
        FleetKeys.ApplyMotions(list, Map);

        Assert.Contains(expected, list.KeyBindings.GetCommands(Map.KeyFor(action)));
    }

    [Fact]
    public void Open_maps_the_configured_key_to_accept()
    {
        var list = new ListView();
        FleetKeys.ApplyOpen(list, Map);

        Assert.Contains(
            Command.Accept,
            list.KeyBindings.GetCommands(Map.KeyFor(FleetAction.OpenProject)));
    }

    [Fact]
    public void Arrow_keys_keep_working_alongside_the_vim_motions()
    {
        var list = new ListView();
        FleetKeys.ApplyMotions(list, Map);

        Assert.Contains(Command.Down, list.KeyBindings.GetCommands(Key.CursorDown));
        Assert.Contains(Command.Up, list.KeyBindings.GetCommands(Key.CursorUp));
    }

    [Fact]
    public void A_rebound_motion_is_what_gets_applied()
    {
        var keymap = new Keymap(Map.Config.With(FleetAction.MoveDown, "z"));
        var list = new ListView();

        FleetKeys.ApplyMotions(list, keymap);

        Assert.Contains(Command.Down, list.KeyBindings.GetCommands(Key.Z));
    }
}
