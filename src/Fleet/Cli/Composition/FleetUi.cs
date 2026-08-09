using Fleet.Features.Menu.ShowMenu;
using Fleet.Shared.Keymap.Enums;
using Fleet.Ui;
using Terminal.Gui.App;

namespace Fleet.Cli.Composition;

public static class FleetUi
{
    public static IApplication Start()
    {
        var app = Application.Create().Init();
        FleetTheme.Register();

        return app;
    }

    public static FleetAction Menu(
        IApplication app, Keymap keymap, IReadOnlyList<FleetAction> actions) =>
        ShowMenuView.Show(app, keymap, new ShowMenuHandler(keymap).Items(actions));
}
