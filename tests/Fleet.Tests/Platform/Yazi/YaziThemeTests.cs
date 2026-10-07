using Fleet.Platform.Yazi;
using Fleet.Ports.Themes.Enums;
using Fleet.Shared.Themes;

namespace Fleet.Tests.Platform.Yazi;

public sealed class YaziThemeTests : IDisposable
{
    private readonly string _config = Path.Combine(Path.GetTempPath(), "fleet-tests", Path.GetRandomFileName());

    public void Dispose()
    {
        if (Directory.Exists(_config))
        {
            Directory.Delete(_config, recursive: true);
        }
    }

    private string ThemeFile => Path.Combine(_config, "theme.toml");

    [Fact]
    public void The_theme_starts_with_fleets_header()
    {
        var toml = YaziTheme.Generate(BuiltInThemes.CatppuccinMocha);

        Assert.True(YaziTheme.IsFleets(toml));
        Assert.DoesNotContain("\r", toml);
    }

    // Section names follow yazi's preset theme-dark.toml (yazi 26.x): [mgr], not the old [manager].
    [Theory]
    [InlineData("[mgr]")]
    [InlineData("[tabs]")]
    [InlineData("[mode]")]
    [InlineData("[indicator]")]
    [InlineData("[status]")]
    [InlineData("[which]")]
    [InlineData("[confirm]")]
    [InlineData("[spot]")]
    [InlineData("[notify]")]
    [InlineData("[pick]")]
    [InlineData("[input]")]
    [InlineData("[cmp]")]
    [InlineData("[tasks]")]
    [InlineData("[help]")]
    [InlineData("[filetype]")]
    public void It_styles_each_yazi_section(string section) =>
        Assert.Contains($"\n{section}\n", YaziTheme.Generate(BuiltInThemes.CatppuccinMocha));

    [Fact]
    public void Colors_come_from_the_palette()
    {
        var theme = BuiltInThemes.Find("nord")!;
        var toml = YaziTheme.Generate(theme);

        Assert.Contains($"border_style = {{ fg = \"{theme.Surface1}\" }}", toml);
        Assert.Contains($"normal_main = {{ fg = \"{theme.Base}\", bg = \"{theme.Blue}\", bold = true }}", toml);
        Assert.Contains($"{{ url = \"*/\", fg = \"{theme.Blue}\" }}", toml);
        Assert.DoesNotContain("[manager]", toml);
    }

    [Fact]
    public void A_missing_file_is_written()
    {
        Directory.CreateDirectory(_config);
        var applied = new YaziThemeTarget(_config).Apply(BuiltInThemes.CatppuccinMocha);

        Assert.Equal(ThemeOutcome.Applied, applied.Outcome);
        Assert.Equal(YaziTheme.Generate(BuiltInThemes.CatppuccinMocha), File.ReadAllText(ThemeFile));
    }

    [Fact]
    public void A_file_fleet_wrote_is_replaced_and_an_unchanged_one_reported()
    {
        Directory.CreateDirectory(_config);
        var target = new YaziThemeTarget(_config);

        target.Apply(BuiltInThemes.CatppuccinMocha);

        Assert.Equal(ThemeOutcome.Applied, target.Apply(BuiltInThemes.Find("dracula")!).Outcome);
        Assert.Equal(ThemeOutcome.Unchanged, target.Apply(BuiltInThemes.Find("dracula")!).Outcome);
        Assert.Contains("'dracula'", File.ReadAllText(ThemeFile));
    }

    [Fact]
    public void Without_a_yazi_config_folder_nothing_is_created()
    {
        var applied = new YaziThemeTarget(_config).Apply(BuiltInThemes.CatppuccinMocha);

        Assert.Equal(ThemeOutcome.Skipped, applied.Outcome);
        Assert.False(Directory.Exists(_config));
    }

    [Fact]
    public void A_users_own_theme_is_never_overwritten()
    {
        Directory.CreateDirectory(_config);
        File.WriteAllText(ThemeFile, "[flavor]\ndark = \"catppuccin-mocha\"\n");

        var applied = new YaziThemeTarget(_config).Apply(BuiltInThemes.CatppuccinMocha);

        Assert.Equal(ThemeOutcome.Skipped, applied.Outcome);
        Assert.Equal("[flavor]\ndark = \"catppuccin-mocha\"\n", File.ReadAllText(ThemeFile));
    }

    [Fact]
    public void The_config_folder_honours_yazi_config_home() =>
        Assert.Equal("/x/yazi", YaziConfigHome.Resolve(" /x/yazi ", windows: true, "appdata", "home"));

    [Fact]
    public void On_windows_the_config_folder_is_under_appdata() =>
        Assert.Equal(
            Path.Combine("appdata", "yazi", "config"),
            YaziConfigHome.Resolve(null, windows: true, "appdata", "home"));

    [Fact]
    public void Elsewhere_the_config_folder_is_under_dot_config() =>
        Assert.Equal(
            Path.Combine("home", ".config", "yazi"),
            YaziConfigHome.Resolve("", windows: false, "appdata", "home"));
}
