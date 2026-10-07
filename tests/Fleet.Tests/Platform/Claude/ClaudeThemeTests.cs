using System.Text.Json;
using Fleet.Platform.Claude;
using Fleet.Ports.Themes.Enums;
using Fleet.Shared.Themes;

namespace Fleet.Tests.Platform.Claude;

public sealed class ClaudeThemeTests : IDisposable
{
    private readonly string _config = Path.Combine(Path.GetTempPath(), "fleet-tests", Path.GetRandomFileName());

    public void Dispose()
    {
        if (Directory.Exists(_config))
        {
            Directory.Delete(_config, recursive: true);
        }
    }

    private string SettingsFile => Path.Combine(_config, "settings.json");

    private string ThemeFile => Path.Combine(_config, "themes", "fleet.json");

    [Theory]
    [InlineData("catppuccin-mocha", "dark")]
    [InlineData("catppuccin-latte", "light")]
    [InlineData("gruvbox-light", "light")]
    [InlineData("nord", "dark")]
    public void The_custom_theme_starts_from_dark_or_light_to_match(string name, string expected) =>
        Assert.Equal(expected, ClaudeTheme.Generate(BuiltInThemes.Find(name)!).Base);

    [Fact]
    public void Every_override_is_a_hex_color_from_the_palette()
    {
        var theme = BuiltInThemes.CatppuccinMocha;
        var file = ClaudeTheme.Generate(theme);

        Assert.All(file.Overrides.Values, v => Assert.Equal(v, HexColor.Normalize(v)));
        Assert.Equal(theme.Lavender, file.Overrides["claude"]);
        Assert.Equal(theme.Text, file.Overrides["text"]);
        Assert.Equal(theme.Red, file.Overrides["error"]);
        Assert.Contains(theme.Title, file.Name);
    }

    [Fact]
    public void Applying_writes_the_theme_file_and_selects_it_keeping_other_settings()
    {
        Directory.CreateDirectory(_config);
        File.WriteAllText(
            SettingsFile,
            """{ "theme": "dark", "model": "opus", "permissions": { "allow": ["Bash(ls)"] } }""");

        var applied = new ClaudeThemeTarget(_config).Apply(BuiltInThemes.CatppuccinMocha);

        Assert.Equal(ThemeOutcome.Applied, applied.Outcome);

        using var settings = JsonDocument.Parse(File.ReadAllText(SettingsFile));
        Assert.Equal("custom:fleet", settings.RootElement.GetProperty("theme").GetString());
        Assert.Equal("opus", settings.RootElement.GetProperty("model").GetString());
        Assert.Equal(
            "Bash(ls)",
            settings.RootElement.GetProperty("permissions").GetProperty("allow")[0].GetString());

        using var theme = JsonDocument.Parse(File.ReadAllText(ThemeFile));
        Assert.Equal("dark", theme.RootElement.GetProperty("base").GetString());
        Assert.Equal(
            BuiltInThemes.CatppuccinMocha.Lavender,
            theme.RootElement.GetProperty("overrides").GetProperty("claude").GetString());
    }

    [Fact]
    public void Applying_twice_changes_nothing_the_second_time()
    {
        Directory.CreateDirectory(_config);
        var target = new ClaudeThemeTarget(_config);

        target.Apply(BuiltInThemes.CatppuccinMocha);
        var before = File.ReadAllText(SettingsFile);

        Assert.Equal(ThemeOutcome.Unchanged, target.Apply(BuiltInThemes.CatppuccinMocha).Outcome);
        Assert.Equal(before, File.ReadAllText(SettingsFile));
    }

    [Fact]
    public void Switching_theme_rewrites_the_theme_file()
    {
        Directory.CreateDirectory(_config);
        var target = new ClaudeThemeTarget(_config);

        target.Apply(BuiltInThemes.CatppuccinMocha);

        Assert.Equal(ThemeOutcome.Applied, target.Apply(BuiltInThemes.Find("catppuccin-latte")!).Outcome);
        Assert.Contains("\"light\"", File.ReadAllText(ThemeFile));
    }

    // settings.json is often under dotfile management: only the theme value may change.
    [Fact]
    public void Only_the_theme_value_changes_in_the_settings_file()
    {
        Directory.CreateDirectory(_config);
        File.WriteAllText(
            SettingsFile,
            """
            {
              "model": "opus",
              "theme": "dark",
              "statusLine": { "command": "echo é & <ok>" }
            }
            """);

        new ClaudeThemeTarget(_config).Apply(BuiltInThemes.CatppuccinMocha);

        var text = File.ReadAllText(SettingsFile);
        Assert.True(text.IndexOf("\"model\"", StringComparison.Ordinal) < text.IndexOf("\"theme\"", StringComparison.Ordinal));
        Assert.Contains("echo é & <ok>", text);
        Assert.Contains("\"theme\": \"custom:fleet\"", text);
        Assert.DoesNotContain("enabledMcpjsonServers", text);
    }

    [Fact]
    public void Without_a_claude_config_folder_nothing_is_created()
    {
        var applied = new ClaudeThemeTarget(_config).Apply(BuiltInThemes.CatppuccinMocha);

        Assert.Equal(ThemeOutcome.Skipped, applied.Outcome);
        Assert.False(Directory.Exists(_config));
    }

    [Fact]
    public void Unreadable_settings_are_left_untouched()
    {
        Directory.CreateDirectory(_config);
        File.WriteAllText(SettingsFile, "{ not json");

        var applied = new ClaudeThemeTarget(_config).Apply(BuiltInThemes.CatppuccinMocha);

        Assert.Equal(ThemeOutcome.Failed, applied.Outcome);
        Assert.Equal("{ not json", File.ReadAllText(SettingsFile));
    }
}
