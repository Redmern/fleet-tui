using Fleet.Shared.Themes;
using Fleet.Ui;
using Fleet.Ui.Constants;
using Terminal.Gui.Configuration;
using Terminal.Gui.Drawing;

using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Fleet.Tests.Ui;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PaletteCollection
{
    public const string Name = "palette";
}

[Collection(PaletteCollection.Name)]
public sealed class FleetPaletteTests : IDisposable
{
    private static readonly ThemePalette Latte = BuiltInThemes.Find("catppuccin-latte")!;

    public void Dispose() => FleetTheme.Use(BuiltInThemes.CatppuccinMocha);

    [Fact]
    public void The_palette_defaults_to_catppuccin_mocha() =>
        Assert.Equal("#1e1e2e", BuiltInThemes.CatppuccinMocha.Base);

    [Fact]
    public void Using_a_theme_recolors_the_ink()
    {
        FleetTheme.Use(Latte);

        var basis = new Attribute(new Color(Latte.Text), new Color(Latte.Base), TextStyle.None);

        Assert.Equal(new Color(Latte.Blue), FleetInk.For(FleetTones.Key, basis).Foreground);
        Assert.Equal(new Color(Latte.Surface0), FleetInk.For(FleetTones.ChipKey, basis).Background);
    }

    [Fact]
    public void Using_a_theme_re_registers_the_schemes()
    {
        FleetTheme.Use(Latte);

        var screen = SchemeManager.GetScheme(FleetSchemes.Screen);

        Assert.Equal(new Color(Latte.Base), screen.Normal.Background);
        Assert.Equal(new Color(Latte.Text), screen.Normal.Foreground);
        Assert.Same(Latte, FleetPalette.Current);
    }
}
