using Fleet.Platform.Themes;
using Fleet.Shared.Themes;
using Fleet.Tests.Platform.Storage;

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

    [Fact]
    public void Saved_overrides_are_written_as_given_under_the_theme_name()
    {
        var store = new FileThemeStore(_config);

        var file = store.SaveCustom("mine", "inherits = \"nord\"\nblue = \"#010203\"\n");

        Assert.Equal(Path.Combine(store.ThemesDirectory, "mine.toml"), file);
        Assert.Equal("inherits = \"nord\"\nblue = \"#010203\"\n", File.ReadAllText(file));
        Assert.Equal("#010203", Assert.Single(store.Custom()).Blue);
    }

    [Fact]
    public void A_custom_theme_inherits_from_another_custom_theme()
    {
        var store = new FileThemeStore(_config);

        store.SaveCustom("parent", "inherits = \"nord\"\nblue = \"#010203\"\n");
        store.SaveCustom("child", "inherits = \"parent\"\nred = \"#040506\"\n");

        var child = store.Custom().Single(t => t.Name == "child");

        Assert.Equal("#010203", child.Blue);
        Assert.Equal("#040506", child.Red);
        Assert.Equal(BuiltInThemes.Find("nord")!.Base, child.Base);
    }

    [Fact]
    public void Inheritance_cycles_end_at_the_built_in_or_mocha()
    {
        var store = new FileThemeStore(_config);

        store.SaveCustom("nord", "inherits = \"nord\"\nblue = \"#010203\"\n");
        store.SaveCustom("a", "inherits = \"b\"\n");
        store.SaveCustom("b", "inherits = \"a\"\n");

        var themes = store.Custom();

        Assert.Equal(BuiltInThemes.Find("nord")!.Base, themes.Single(t => t.Name == "nord").Base);
        Assert.Equal(BuiltInThemes.CatppuccinMocha.Base, themes.Single(t => t.Name == "a").Base);
    }

    [Fact]
    public void A_theme_whose_base_was_deleted_falls_back_to_mocha()
    {
        var store = new FileThemeStore(_config);

        store.SaveCustom("parent", "inherits = \"nord\"\n");
        store.SaveCustom("child", "inherits = \"parent\"\nred = \"#040506\"\n");
        store.DeleteCustom("parent");

        var child = Assert.Single(store.Custom());

        Assert.Equal(BuiltInThemes.CatppuccinMocha.Base, child.Base);
        Assert.Equal("#040506", child.Red);
    }

    [Fact]
    public void Deleting_a_custom_theme_removes_its_file_and_a_missing_one_is_no_error()
    {
        var store = new FileThemeStore(_config);
        var file = store.SaveCustom("mine", "inherits = \"nord\"\n");

        Assert.True(store.DeleteCustom("mine"));
        Assert.False(File.Exists(file));
        Assert.False(store.DeleteCustom("mine"));
        Assert.Empty(store.Custom());
    }

    // Windows refuses to replace a file another process (Defender, the indexer) has open; the write waits it out.
    [Fact]
    public void An_atomic_write_waits_out_a_target_briefly_held_by_another_process()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var file = Path.Combine(_config, "held.txt");
        Directory.CreateDirectory(_config);
        File.WriteAllText(file, "old");

        using (HeldShut.For(file, TimeSpan.FromMilliseconds(300)))
        {
            FileThemeStore.WriteAtomically(file, "new");
        }

        Assert.Equal("new", File.ReadAllText(file));
        Assert.Equal([file], Directory.GetFiles(_config));
    }
}
