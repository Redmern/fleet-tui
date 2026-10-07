using Fleet.Features.Menu.ShowMenu;
using Fleet.Features.Menu.ShowMenu.Models;
using Fleet.Shared.Constants;
using Fleet.Shared.Keymap.Enums;
using Fleet.Shared.Settings.Enums;
using Fleet.Ui;
using Terminal.Gui.App;

namespace Fleet.Cli.Composition;

public static class FleetUi
{
    public static IApplication Start()
    {
        var app = Application.Create().Init();
        FleetTheme.Use(Adapters.Themes().Active());
        Follow(app);
        FleetKeyHints.Attach(app, () => new Keymap(Adapters.Keymaps().Load()), Adapters.ShowMenuKeys);
        FleetButtonHints.Attach(app, Adapters.ButtonHints);

        if (FloatPane.Inside)
        {
            FloatScreens.Fit = EmbeddedWiring.FitOwnFloat;
            FloatScreens.Hold = EmbeddedWiring.HoldOwnFloat;
            FloatBorder.Enable(app, EmbeddedWiring.PublishOwnFloatButtons);
        }

        return app;
    }

    private static IApplication? _themed;

    private static IDisposable? _themeWatch;

    private static void Follow(IApplication app)
    {
        _themed = app;
        _themeWatch ??= ThemeWiring.Follow(theme =>
        {
            try
            {
                _themed?.Invoke(() =>
                {
                    FleetTheme.Use(theme);
                    _themed?.LayoutAndDraw(true);
                });
            }
            catch (Exception e) when (e is ObjectDisposedException or InvalidOperationException)
            {
            }
        });
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
        Func<bool> showKeys,
        Func<ButtonHints> buttonHints) =>
        ShowMenuView.Show(
            app,
            keymap,
            FleetMenus.Title(submenu),
            new ShowMenuHandler(keymap).Items(sections, value),
            toggle,
            showKeys,
            buttonHints);
}
