using Fleet.Features.Menu.ShowMenu;
using Fleet.Features.Menu.ShowMenu.Models;
using Fleet.Shared.Keymap.Enums;
using Fleet.Ui;
using Terminal.Gui.Input;

namespace Fleet.Tests.Ui.Harnesses;

[Collection(ModalDepthCollection.Name)]
public sealed class MenuHarnessTests
{
    private static readonly IReadOnlyList<FleetMenuItem> Items =
    [
        new(FleetAction.ListAgents, "Agents", "l"),
        new(FleetAction.TodoBoard, "Todo board", "t"),
        new(FleetAction.OpenSettings, "Settings", "s", OpensMore: true),
    ];

    private static HeadlessRun<FleetAction> Run(params Key[] keys) =>
        HeadlessUi.Run(app => ShowMenuView.Show(app, Keymap.Default, Items), keys);

    [Fact]
    public void Enter_chooses_the_first_item()
    {
        Assert.Equal(FleetAction.ListAgents, Run(Key.Enter).Result);
    }

    [Fact]
    public void Down_then_enter_chooses_the_next_item()
    {
        Assert.Equal(FleetAction.TodoBoard, Run(Key.CursorDown, Key.Enter).Result);
        Assert.Equal(FleetAction.OpenSettings, Run(Key.CursorDown, Key.CursorDown, Key.Enter).Result);
    }

    [Theory]
    [InlineData(FleetAction.ListAgents)]
    [InlineData(FleetAction.TodoBoard)]
    [InlineData(FleetAction.OpenSettings)]
    public void An_items_default_key_chooses_it_without_moving(FleetAction action)
    {
        var run = Run(Keymap.Default.KeyFor(action));

        Assert.Equal(action, run.Result);
        Assert.False(run.LeftOpen);
    }

    [Fact]
    public void The_close_key_leaves_the_menu_with_nothing_chosen()
    {
        var run = Run(Keymap.Default.KeyFor(FleetAction.Close));

        Assert.Equal(FleetAction.None, run.Result);
        Assert.False(run.LeftOpen);
        Assert.False(run.WentBack);
    }

    [Fact]
    public void Backspace_leaves_the_menu_and_goes_back()
    {
        var run = Run(Key.Backspace);

        Assert.Equal(FleetAction.None, run.Result);
        Assert.True(run.WentBack);
    }

    [Fact]
    public void A_key_no_item_owns_leaves_the_menu_open()
    {
        var run = Run(Key.Z);

        Assert.Equal(FleetAction.None, run.Result);
        Assert.True(run.LeftOpen);
    }
}
