using Fleet.Ui;
using Terminal.Gui.Input;
using Terminal.Gui.Views;

namespace Fleet.Tests.Ui;

public class FleetKeysTests
{
    [Fact]
    public void ApplyMotions_does_not_throw_on_keys_the_widget_already_binds()
    {
        var list = new ListView();

        FleetKeys.ApplyMotions(list);
        FleetKeys.ApplyOpen(list);
    }

    [Fact]
    public void ApplyMotions_is_idempotent()
    {
        var list = new ListView();

        FleetKeys.ApplyMotions(list);
        FleetKeys.ApplyMotions(list);
    }

    [Theory]
    [InlineData("Down", Command.Down)]
    [InlineData("Up", Command.Up)]
    [InlineData("First", Command.Start)]
    [InlineData("Last", Command.End)]
    [InlineData("PageDown", Command.PageDown)]
    [InlineData("PageUp", Command.PageUp)]
    public void Each_motion_key_maps_to_its_command(string name, Command expected)
    {
        var list = new ListView();
        FleetKeys.ApplyMotions(list);

        var key = (Key)typeof(FleetKeys).GetField(name)!.GetValue(null)!;

        Assert.Contains(expected, list.KeyBindings.GetCommands(key));
    }

    [Fact]
    public void Open_maps_l_to_accept()
    {
        var list = new ListView();
        FleetKeys.ApplyOpen(list);

        Assert.Contains(Command.Accept, list.KeyBindings.GetCommands(FleetKeys.Open));
    }

    [Fact]
    public void Arrow_keys_keep_working_alongside_the_vim_motions()
    {
        var list = new ListView();
        FleetKeys.ApplyMotions(list);

        Assert.Contains(Command.Down, list.KeyBindings.GetCommands(Key.CursorDown));
        Assert.Contains(Command.Up, list.KeyBindings.GetCommands(Key.CursorUp));
    }
}
