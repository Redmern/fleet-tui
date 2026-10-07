using Fleet.Shared.Constants;
using Fleet.Shared.Themes;
using Fleet.Ui.Constants;
using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Fleet.Ui;

public static class FleetTheme
{
    public const LineStyle Border = LineStyle.Rounded;

    private static readonly System.Text.Rune NoHotKey = (System.Text.Rune)'￿';

    public static void Register() => FleetSchemeRegistry.Register();

    public static void Use(ThemePalette palette)
    {
        FleetPalette.Current = palette;
        FleetSchemeRegistry.Register();
    }

    public static Window Screen(string title) => Floating(new Window
    {
        Title = $" {title} ",
        BorderStyle = FramedPane.Inside ? LineStyle.None : Border,
        SchemeName = FleetSchemes.Screen,
    }, new FloatScreen(title));

    public static Window Overlay(string title, int fitCols = 0, int fitRows = 0) => Floating(new Window
    {
        Title = $" {title} ",
        X = 0,
        Y = 0,
        Width = Dim.Fill(),
        Height = Dim.Fill(),
        BorderStyle = Border,
        SchemeName = FleetSchemes.Screen,
    }, new FloatScreen(title, fitCols, fitRows));

    public static Window Modal(string title, int width, int height) => Floating(new Window
    {
        Title = $" {title} ",
        X = Pos.Center(),
        Y = Pos.Center(),
        Width = width,
        Height = height,
        BorderStyle = Border,
        SchemeName = FleetSchemes.Screen,
    }, new FloatScreen(title, width, height));

    private static Window Floating(Window window, FloatScreen screen)
    {
        if (!FloatPane.Inside)
        {
            return window;
        }

        window.BorderStyle = LineStyle.None;
        window.X = 0;
        window.Y = 0;
        window.Width = Dim.Fill();
        window.Height = Dim.Fill();
        window.IsRunningChanged += (_, running) =>
        {
            var fitted = FloatScreens.Running(screen, running.Value);
            FloatBorder.Run(window, running.Value);

            if (fitted is var (cols, rows))
            {
                window.App?.Driver?.SetScreenSize(cols, rows);
            }
        };

        return window;
    }

    public static Label SectionHeader(Pos x, Pos y, string text) => new()
    {
        X = x,
        Y = y,
        Width = Dim.Fill(1),
        Text = text,
        SchemeName = FleetSchemes.Section,
    };

    public static ListView Rows(Pos x, Pos y, Dim height) => Steady(new FleetList
    {
        X = x,
        Y = y,
        Width = Dim.Fill(1),
        Height = height,
        SchemeName = FleetSchemes.Screen,
    });

    public static ListView CenteredRows(int width, int height, int footer = 0) => Steady(new FleetList
    {
        X = Pos.Center(),
        Y = Pos.Center() - footer / 2,
        Width = width,
        Height = height,
        SchemeName = FleetSchemes.Screen,
    });

    private static ListView Steady(ListView list)
    {
        FleetRows.KeepOffSpacers(list);

        return list;
    }

    public static FleetTabBar TabBar(Pos x, Pos y, IReadOnlyList<string> titles) =>
        new(x, y, titles);

    public static Label Caption(Pos x, Pos y, string text) => new()
    {
        X = x,
        Y = y,
        Text = text,
        SchemeName = FleetSchemes.Screen,
        HotKeySpecifier = NoHotKey,
    };

    public static Label StatusLine(Pos y) => new()
    {
        X = 1,
        Y = y,
        Width = Dim.Fill(2),
        Text = string.Empty,
        TextAlignment = Alignment.End,
        SchemeName = FleetSchemes.Status,
    };

    public static TextField Field(Pos x, Pos y, string text = "") => new()
    {
        X = x,
        Y = y,
        Width = Dim.Fill(2),
        Text = text,
        SchemeName = FleetSchemes.Screen,
    };

    public static Button Choice(Pos x, Pos y, string text) => new()
    {
        X = x,
        Y = y,
        Text = text,
        NoDecorations = false,
        ShadowStyle = ShadowStyles.None,
        HotKeySpecifier = NoHotKey,
        SchemeName = FleetSchemes.Accent,
    };

    public static CheckBox Toggle(Pos x, Pos y, string text) => new()
    {
        X = x,
        Y = y,
        Text = text,
        SchemeName = FleetSchemes.Screen,
    };

    public static Button Submit(Pos x, Pos y, string text) => new()
    {
        X = x,
        Y = y,
        Text = text,
        ShadowStyle = ShadowStyles.None,
        HotKeySpecifier = NoHotKey,
        SchemeName = FleetSchemes.Accent,
    };

    public static Button Primary(Pos x, Pos y, string text) => new()
    {
        X = x,
        Y = y,
        Text = text,
        IsDefault = true,
        ShadowStyle = ShadowStyles.None,
        HotKeySpecifier = NoHotKey,
        SchemeName = FleetSchemes.Accent,
    };

    public static Button Secondary(Pos x, Pos y, string text) => new()
    {
        X = x,
        Y = y,
        Text = text,
        ShadowStyle = ShadowStyles.None,
        HotKeySpecifier = NoHotKey,
        SchemeName = FleetSchemes.Screen,
    };

    public static Label ErrorText(Pos x, Pos y) => new()
    {
        X = x,
        Y = y,
        Width = Dim.Fill(2),
        Text = string.Empty,
        SchemeName = FleetSchemes.Error,
    };

    public static Label ErrorLine(Pos x, Pos y, string text) => new()
    {
        X = x,
        Y = y,
        Width = Dim.Fill(2),
        Text = text,
        SchemeName = FleetSchemes.Error,
    };
}
