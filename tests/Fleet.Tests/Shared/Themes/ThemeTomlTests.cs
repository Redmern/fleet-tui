using Fleet.Shared.Themes;

namespace Fleet.Tests.Shared.Themes;

public class ThemeTomlTests
{
    [Fact]
    public void Parse_reads_sections_quotes_and_strips_comments()
    {
        var values = ThemeToml.Parse("""
            # a theme
            title = "Mine # not a comment"
            light = true  # trailing
            [colors.primary]
            background = '0x1E1E2E'
            """);

        Assert.Equal("Mine # not a comment", values["title"]);
        Assert.Equal("true", values["light"]);
        Assert.Equal("0x1E1E2E", values["colors.primary.background"]);
    }

    [Theory]
    [InlineData("#ABCDEF", "#abcdef")]
    [InlineData("0x123abc", "#123abc")]
    [InlineData("123abc", "#123abc")]
    [InlineData("#12345", null)]
    [InlineData("red", null)]
    public void Hex_colors_normalize_to_lower_case_with_a_hash(string input, string? expected) =>
        Assert.Equal(expected, HexColor.Normalize(input));

    [Fact]
    public void Mix_blends_channel_by_channel()
    {
        Assert.Equal("#808080", HexColor.Mix("#000000", "#ffffff", 0.5));
        Assert.Equal("#000000", HexColor.Mix("#000000", "#ffffff", 0));
    }

    [Fact]
    public void A_written_theme_reads_back_as_the_same_palette()
    {
        foreach (var theme in BuiltInThemes.All)
        {
            var back = CustomTheme.FromToml(theme.Name, ThemeToml.Write(theme));

            Assert.Equal(theme.Name, back.Name);
            Assert.Equal(theme.Title, back.Title);
            Assert.Equal(theme.Light, back.Light);
            Assert.All(ThemePalette.RoleNames, r => Assert.Equal(theme.Role(r), back.Role(r)));
            Assert.Equal(theme.Ansi, back.Ansi);
            Assert.Equal(theme.Brights, back.Brights);
        }
    }

    [Fact]
    public void A_custom_theme_inherits_what_it_leaves_out()
    {
        var theme = CustomTheme.FromToml("My Theme", """
            inherits = "nord"
            blue = "#010203"
            color9 = "#0a0b0c"
            """);

        var nord = BuiltInThemes.Find("nord")!;

        Assert.Equal("my-theme", theme.Name);
        Assert.Equal("My Theme", theme.Title);
        Assert.Equal("#010203", theme.Blue);
        Assert.Equal(nord.Base, theme.Base);
        Assert.Equal("#0a0b0c", theme.Brights[1]);
        Assert.Equal(nord.Ansi, theme.Ansi);
    }

    [Fact]
    public void A_custom_theme_without_inherits_starts_from_mocha_and_ignores_bad_colors()
    {
        var theme = CustomTheme.FromToml("x", "base = \"not a color\"");

        Assert.Equal(BuiltInThemes.CatppuccinMocha.Base, theme.Base);
    }

    [Fact]
    public void Overrides_name_the_base_and_write_only_the_changed_colors()
    {
        var nord = BuiltInThemes.Find("nord")!;
        var theme = nord with { Title = "Mine", Blue = "#010203" };

        var text = ThemeToml.WriteOverrides(theme, nord);
        var values = ThemeToml.Parse(text);

        Assert.Equal("nord", values["inherits"]);
        Assert.Equal("Mine", values["title"]);
        Assert.Equal("#010203", values["blue"]);
        Assert.Equal(4, values.Count);
    }

    [Fact]
    public void Overrides_take_light_from_the_resulting_base_color()
    {
        var mocha = BuiltInThemes.CatppuccinMocha;

        Assert.Equal("true", ThemeToml.Parse(ThemeToml.WriteOverrides(mocha with { Base = "#f0f0f0" }, mocha))["light"]);
        Assert.Equal("false", ThemeToml.Parse(ThemeToml.WriteOverrides(mocha, mocha))["light"]);
    }

    [Fact]
    public void Ansi_and_bright_overrides_round_trip_and_the_rest_inherits()
    {
        var nord = BuiltInThemes.Find("nord")!;
        var ansi = nord.Ansi.ToArray();
        var brights = nord.Brights.ToArray();
        ansi[0] = "#111111";
        brights[7] = "#222222";
        var theme = nord with { Name = "mine", Ansi = ansi, Brights = brights };

        var text = ThemeToml.WriteOverrides(theme, nord);
        var back = CustomTheme.FromToml("mine", text);

        Assert.Equal("#111111", ThemeToml.Parse(text)["color0"]);
        Assert.Equal("#222222", ThemeToml.Parse(text)["color15"]);
        Assert.DoesNotContain("color1", ThemeToml.Parse(text).Keys);
        Assert.Equal(ansi, back.Ansi);
        Assert.Equal(brights, back.Brights);
        Assert.All(ThemePalette.RoleNames, r => Assert.Equal(nord.Role(r), back.Role(r)));
    }

    [Fact]
    public void A_custom_theme_can_inherit_from_a_theme_the_resolver_finds()
    {
        var parent = BuiltInThemes.CatppuccinMocha with { Name = "parent", Blue = "#123456" };

        var theme = CustomTheme.FromToml("child", "inherits = \"parent\"\nred = \"#654321\"", n => n == "parent" ? parent : null);

        Assert.Equal("#123456", theme.Blue);
        Assert.Equal("#654321", theme.Red);
    }
}
