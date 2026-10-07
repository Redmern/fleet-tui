using Fleet.Shared.Themes;
using Fleet.Shared.Themes.Models;

namespace Fleet.Tests.Shared.Themes;

public class OmarchyThemeTests
{
    private const string DarkColors = """
        accent = "#e68e0d"
        cursor = "#eaeaea"
        foreground = "#bebebe"
        background = "#121212"
        color0 = "#333333"
        color1 = "#d35f5f"
        color2 = "#ffc107"
        color3 = "#b91c1c"
        color4 = "#e68e0d"
        color5 = "#d35f5f"
        color6 = "#bebebe"
        color7 = "#bebebe"
        color8 = "#8a8a8d"
        color9 = "#f59e0b"
        color10 = "#b91c1c"
        color11 = "#f59e0b"
        color12 = "#e68e0d"
        color13 = "#f59e0b"
        color14 = "#eaeaea"
        color15 = "#ffffff"
        """;

    private const string LightAlacritty = """
        [colors.primary]
        background = "#faf4ed"
        foreground = "#575279"

        [colors.normal]
        black = "#f2e9e1"
        red = "#b4637a"
        green = "#286983"
        yellow = "#ea9d34"
        blue = "#56949f"
        magenta = "#907aa9"
        cyan = "#d7827e"
        white = "#575279"
        """;

    [Theory]
    [InlineData("catppuccin", "catppuccin-mocha")]
    [InlineData("catppuccin-latte", "catppuccin-latte")]
    [InlineData("gruvbox", "gruvbox-dark")]
    [InlineData("tokyo-night", "tokyo-night")]
    [InlineData("nord", "nord")]
    [InlineData("kanagawa", "kanagawa")]
    [InlineData("everforest", "everforest")]
    public void A_known_theme_name_maps_to_the_built_in_theme(string omarchy, string expected) =>
        Assert.Equal(expected, OmarchyTheme.Resolve(new OmarchySnapshot(omarchy, null, null, false)).Name);

    [Fact]
    public void A_light_theme_does_not_map_to_a_dark_built_in_of_the_same_name()
    {
        var theme = OmarchyTheme.Resolve(new OmarchySnapshot("rose-pine", null, LightAlacritty, true));

        Assert.Equal(OmarchyTheme.CustomName, theme.Name);
        Assert.True(theme.Light);
        Assert.Equal("#faf4ed", theme.Base);
        Assert.Equal("#575279", theme.Text);
        Assert.Equal("#56949f", theme.Blue);
    }

    [Fact]
    public void An_unknown_theme_is_derived_from_colors_toml()
    {
        var theme = OmarchyTheme.Resolve(new OmarchySnapshot("matte-black", DarkColors, null, false));

        Assert.Equal(OmarchyTheme.CustomName, theme.Name);
        Assert.Equal("Omarchy: matte-black", theme.Title);
        Assert.False(theme.Light);
        Assert.Equal("#121212", theme.Base);
        Assert.Equal("#bebebe", theme.Text);
        Assert.Equal("#e68e0d", theme.Lavender);
        Assert.Equal("#eaeaea", theme.Cursor);
        Assert.Equal("#ffffff", theme.Brights[7]);
        Assert.True(HexColor.IsLight(theme.Text));
        Assert.False(HexColor.IsLight(theme.Crust));
    }

    [Fact]
    public void An_unknown_theme_without_colors_cannot_be_resolved() =>
        Assert.Throws<InvalidOperationException>(
            () => OmarchyTheme.Resolve(new OmarchySnapshot("mystery", null, null, false)));
}
