using System.Text.RegularExpressions;
using Fleet.Shared.Themes;

namespace Fleet.Tests.Shared.Themes;

public partial class BuiltInThemesTests
{
    [GeneratedRegex("^#[0-9a-f]{6}$")]
    private static partial Regex Hex();

    [Fact]
    public void Fifteen_themes_ship_with_catppuccin_mocha_first_and_default()
    {
        Assert.Equal(15, BuiltInThemes.All.Count);
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

        Assert.Equal(["catppuccin-latte", "gruvbox-light", "solarized-light"], light);
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
