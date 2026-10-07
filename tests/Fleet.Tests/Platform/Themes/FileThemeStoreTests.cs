using Fleet.Platform.Themes;
using Fleet.Shared.Themes;

namespace Fleet.Tests.Platform.Themes;

public sealed class FileThemeStoreTests : IDisposable
{
    private readonly string _config = Path.Combine(Path.GetTempPath(), "fleet-tests", Path.GetRandomFileName());

    public void Dispose()
    {
        if (Directory.Exists(_config))
        {
            Directory.Delete(_config, recursive: true);
        }
    }

    [Fact]
    public void No_file_means_no_current_theme() =>
        Assert.Null(new FileThemeStore(_config).CurrentName());

    [Fact]
    public void The_current_theme_is_a_one_line_file_of_its_name()
    {
        var store = new FileThemeStore(_config);

        store.SaveCurrent("nord");

        Assert.Equal("nord\n", File.ReadAllText(Path.Combine(_config, "current-theme")));
        Assert.Equal("nord", store.CurrentName());
        Assert.False(File.Exists(store.CurrentFile + ".tmp"));
    }

    [Fact]
    public void Other_apps_may_write_the_file_with_comments_and_blank_lines()
    {
        Directory.CreateDirectory(_config);
        File.WriteAllText(Path.Combine(_config, "current-theme"), "# written by my-app\r\n\r\n  dracula  \r\n");

        Assert.Equal("dracula", new FileThemeStore(_config).CurrentName());
    }

    [Fact]
    public void Custom_themes_are_the_toml_files_in_the_themes_folder()
    {
        var store = new FileThemeStore(_config);

        Directory.CreateDirectory(store.ThemesDirectory);
        File.WriteAllText(Path.Combine(store.ThemesDirectory, "Mine.toml"), "inherits = \"nord\"\nbase = \"#000000\"\n");
        File.WriteAllText(Path.Combine(store.ThemesDirectory, "notes.txt"), "ignored");

        var theme = Assert.Single(store.Custom());

        Assert.Equal("mine", theme.Name);
        Assert.Equal("#000000", theme.Base);
    }

    [Fact]
    public void A_saved_custom_theme_reads_back()
    {
        var store = new FileThemeStore(_config);
        var nord = BuiltInThemes.Find("nord")! with { Name = "omarchy", Title = "Omarchy: nord" };

        var file = store.SaveCustom(nord);

        Assert.Equal(Path.Combine(store.ThemesDirectory, "omarchy.toml"), file);
        Assert.Equal(nord.Base, Assert.Single(store.Custom()).Base);
    }
}
