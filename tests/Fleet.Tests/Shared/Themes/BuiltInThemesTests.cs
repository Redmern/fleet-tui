using System.Text.RegularExpressions;
using Fleet.Shared.Themes;

namespace Fleet.Tests.Shared.Themes;

public partial class BuiltInThemesTests
{
    [GeneratedRegex("^#[0-9a-f]{6}$")]
    private static partial Regex Hex();

    [Fact]
    public void Thirty_three_themes_ship_with_catppuccin_mocha_first_and_default()
    {
        Assert.Equal(33, BuiltInThemes.All.Count);
        Assert.Same(BuiltInThemes.CatppuccinMocha, BuiltInThemes.All[0]);
        Assert.Equal(BuiltInThemes.DefaultName, BuiltInThemes.CatppuccinMocha.Name);
    }

    [Fact]
    public void Theme_names_are_unique_slugs_of_their_titles()
    {
        Assert.Equal(BuiltInThemes.All.Count, BuiltInThemes.All.Select(t => t.Name).Distinct().Count());

        foreach (var theme in BuiltInThemes.All)
        {
            Assert.Equal(ThemeNames.Normalize(theme.Title), theme.Name);
        }
    }

    [Fact]
    public void Every_color_is_a_lower_case_hex_triplet_with_a_full_ansi_set()
    {
        foreach (var theme in BuiltInThemes.All)
        {
            var colors = ThemePalette.RoleNames.Select(theme.Role).Concat(theme.Ansi).Concat(theme.Brights);

            Assert.All(colors, c => Assert.Matches(Hex(), c));
            Assert.Equal(ThemePalette.AnsiCount, theme.Ansi.Count);
            Assert.Equal(ThemePalette.AnsiCount, theme.Brights.Count);
        }
    }

    [Fact]
    public void The_light_themes_are_marked_light()
    {
        var light = BuiltInThemes.All.Where(t => t.Light).Select(t => t.Name).Order();

        Assert.Equal(
            [
                "ayu-light", "catppuccin-latte", "everforest-light", "flexoki-light", "github-light",
                "gruvbox-light", "kanagawa-lotus", "material-lighter", "modus-operandi", "night-owl-light",
                "one-light", "papercolor-light", "rose-pine-dawn", "solarized-light", "tokyo-night-day",
            ],
            light);
    }

    [Fact]
    public void Every_color_normalizes_to_itself()
    {
        foreach (var theme in BuiltInThemes.All)
        {
            var colors = ThemePalette.RoleNames.Select(theme.Role).Concat(theme.Ansi).Concat(theme.Brights);

            Assert.All(colors, c => Assert.Equal(c, HexColor.Normalize(c)));
        }
    }

    [Fact]
    public void The_light_flag_agrees_with_the_base_color()
    {
        Assert.All(BuiltInThemes.All, t => Assert.True(t.Light == HexColor.IsLight(t.Base), t.Name));
    }

    [Fact]
    public void Text_stands_out_clearly_from_the_base()
    {
        foreach (var theme in BuiltInThemes.All)
        {
            var contrast = Math.Abs(Luma(theme.Text) - Luma(theme.Base));

            Assert.True(contrast > 0.45, $"{theme.Name}: text/base luma difference {contrast:0.00}");
        }
    }

    [Fact]
    public void Subtext_stays_readable_on_the_base()
    {
        foreach (var theme in BuiltInThemes.All)
        {
            var contrast = Math.Abs(Luma(theme.Subtext0) - Luma(theme.Base));

            Assert.True(contrast > 0.3, $"{theme.Name}: subtext/base luma difference {contrast:0.00}");
        }
    }

    private static double Luma(string hex)
    {
        var r = Convert.ToInt32(hex[1..3], 16);
        var g = Convert.ToInt32(hex[3..5], 16);
        var b = Convert.ToInt32(hex[5..7], 16);

        return ((0.299 * r) + (0.587 * g) + (0.114 * b)) / 255;
    }

    [Theory]
    [InlineData("tokyo-night", "tokyo-night")]
    [InlineData("Tokyo Night", "tokyo-night")]
    [InlineData("Rosé Pine", "rose-pine")]
    [InlineData("rose_pine", "rose-pine")]
    [InlineData("  GitHub   Dark ", "github-dark")]
    public void Find_matches_names_and_titles_loosely(string query, string expected) =>
        Assert.Equal(expected, BuiltInThemes.Find(query)?.Name);

    [Fact]
    public void Find_returns_null_for_an_unknown_theme() =>
        Assert.Null(BuiltInThemes.Find("no-such-theme"));

    [Fact]
    public void With_role_replaces_one_role_and_keeps_the_rest()
    {
        var changed = BuiltInThemes.CatppuccinMocha.WithRole("blue", "#000000");

        Assert.Equal("#000000", changed.Blue);
        Assert.Equal(BuiltInThemes.CatppuccinMocha.Base, changed.Base);
    }
}
