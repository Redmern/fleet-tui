using Fleet.Features.Menu.ShowMenu;
using Fleet.Ui;
using Fleet.Ui.Constants;
using Fleet.Ui.Models;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.Views;

namespace Fleet.Tests.Ui;

// AC-4: moving through a menu with the keyboard must not ask fleetd anything (fit, hold and the float
// border buttons are synchronous daemon requests) and must not repaint the whole window.
[Collection(FloatBorderCollection.Name)]
public sealed class NavigationKeyCostTests : IDisposable
{
    private readonly IApplication _app = Application.Create().Init("ansi");
    private readonly Window _window = FleetTheme.Overlay("menu", 52, 14);
    private int _requests;

    public NavigationKeyCostTests()
    {
        FleetKeyHints.Reset();
        FleetButtonHints.Reset();
        FloatBorder.Reset();
        FloatScreens.Fit = (cols, rows) =>
        {
            _requests++;
            return (cols, rows);
        };
        FloatScreens.Hold = () => _requests++;
        FloatBorder.Enable(_app, _ =>
        {
            _requests++;
            return true;
        });
    }

    public void Dispose()
    {
        FloatScreens.Fit = null;
        FloatScreens.Hold = null;
        FloatBorder.Reset();
        FleetButtonHints.Reset();
        FleetKeyHints.Reset();
        _window.Dispose();
        _app.Dispose();
    }

    private ListView Open()
    {
        var (list, bar) = ShowMenuView.Place(20, 5);
        FleetRows.Fill(list, [FleetRow.Plain("new agent"), FleetRow.Plain("dashboard"), FleetRow.Plain("settings")]);
        bar.Show([("enter", FleetIcons.Select, () => { }), ("bksp", FleetIcons.Back, () => { })]);
        FleetKeys.ApplyMotions(list, Keymap.Default);
        _window.Add(list, bar.Root);
        FleetCorners.Attach(_window, () => { });
        _app.Begin(_window);
        FloatScreens.Running(new FloatScreen("menu", 52, 14), true);
        FloatBorder.Run(_window, true);
        list.SetFocus();
        _app.LayoutAndDraw(true);
        _requests = 0;
        return list;
    }

    [Fact]
    public void Moving_down_and_up_asks_fleetd_nothing()
    {
        var list = Open();

        for (var i = 0; i < 6; i++)
        {
            _app.Keyboard.RaiseKeyDownEvent(i % 2 == 0 ? Key.CursorDown : Key.CursorUp);
            _app.LayoutAndDraw();
        }

        Assert.Equal(0, _requests);
        Assert.Equal(0, FleetRows.Selected(list));
    }

    [Fact]
    public void Moving_down_redraws_the_list_but_not_the_window()
    {
        var list = Open();

        _app.Keyboard.RaiseKeyDownEvent(Key.CursorDown);

        Assert.Equal(1, FleetRows.Selected(list));
        Assert.True(list.NeedsDraw);
        Assert.False(_window.NeedsDraw);
        Assert.False(_window.NeedsLayout);
    }
}
