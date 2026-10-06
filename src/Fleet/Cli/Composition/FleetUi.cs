using Fleet.Features.Menu.ShowMenu;
using Fleet.Features.Menu.ShowMenu.Models;
using Fleet.Shared.Constants;
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
        FleetKeyHints.Attach(app, () => new Keymap(Adapters.Keymaps().Load()), Adapters.ShowMenuKeys);

        if (FloatPane.Inside)
        {
            FloatScreens.Fit = EmbeddedWiring.FitOwnFloat;
            FloatScreens.Hold = EmbeddedWiring.HoldOwnFloat;
        }

        return app;
    }

    public static FleetAction Menu(
        IApplication app, Keymap keymap, IReadOnlyList<FleetAction> actions) =>
        ShowMenuView.Show(app, keymap, new ShowMenuHandler(keymap).Items(actions));

    public static FleetAction Menu(
        IApplication app,
        Keymap keymap,
        FleetAction submenu,
        IReadOnlyList<MenuSection> sections,
        Func<FleetAction, string?> value,
        Func<FleetAction, string?> toggle,
        Func<bool> showKeys) =>
        ShowMenuView.Show(
            app,
            keymap,
            FleetMenus.Title(submenu),
            new ShowMenuHandler(keymap).Items(sections, value),
            toggle,
            showKeys);
}
