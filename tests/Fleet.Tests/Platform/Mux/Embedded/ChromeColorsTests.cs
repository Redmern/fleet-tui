using Fleet.Platform.Mux.Embedded.Render;
using Fleet.Shared.Themes;
using Fleet.Tests.Ui;

namespace Fleet.Tests.Platform.Mux.Embedded;

[Collection(PaletteCollection.Name)]
public sealed class ChromeColorsTests : IDisposable
{
    public void Dispose() => Composer.Use(BuiltInThemes.CatppuccinMocha);

    [Fact]
    public void Hex_turns_into_the_same_cell_color_as_its_channels() =>
        Assert.Equal(Cell.Rgb(0x11, 0x11, 0x1b), ChromeColors.Rgb("#11111b"));

    [Fact]
    public void The_chrome_starts_in_catppuccin_mocha()
    {
        Assert.Equal(Cell.Rgb(0xb4, 0xbe, 0xfe), Composer.Lavender);
        Assert.Equal(Cell.Rgb(0x89, 0xb4, 0xfa), Composer.Blue);
    }

    [Fact]
    public void Using_a_theme_recolors_the_chrome()
    {
        var nord = BuiltInThemes.Find("nord")!;

        Composer.Use(nord);

        Assert.Equal(ChromeColors.Rgb(nord.Lavender), Composer.Lavender);
        Assert.Equal(ChromeColors.Rgb(nord.Crust), Composer.Crust);
        Assert.Equal(ChromeColors.Rgb(nord.Cursor), Composer.Flamingo);
    }
}
