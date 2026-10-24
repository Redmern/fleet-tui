using Fleet.Features.Themes.ManageThemes;
using Fleet.Ports.Themes;
using Fleet.Ports.Themes.Enums;
using Fleet.Shared.Themes;
using Fleet.Shared.Themes.Models;

namespace Fleet.Tests.Features.Themes;

public class ManageThemesHandlerTests
{
    private sealed class FakeStore : IThemeStore
    {
        public string? Current { get; set; }

        public List<ThemePalette> Saved { get; } = [];

        public Dictionary<string, string> Texts { get; } = [];

        public string CurrentFile => "current-theme";

        public string? CurrentName() => Current;

        public void SaveCurrent(string name) => Current = name;

        public IReadOnlyList<ThemePalette> Custom() => Saved;

        public string SaveCustom(ThemePalette theme)
        {
            Saved.RemoveAll(t => t.Name == theme.Name);
            Saved.Add(theme);
            return theme.Name + ".toml";
        }

        public string SaveCustom(string name, string toml)
        {
            Texts[name] = toml;
            return SaveCustom(CustomTheme.FromToml(name, toml, n => Saved.FirstOrDefault(t => t.Name == n)));
        }

        public string? CustomText(string name) => Texts.GetValueOrDefault(name);

        public bool DeleteCustom(string name)
        {
            Texts.Remove(name);
            return Saved.RemoveAll(t => t.Name == name) > 0;
        }
    }

    private sealed class FakeOmarchy(OmarchySnapshot? snapshot) : IOmarchy
    {
        public string? Hooked { get; private set; }

        public string HookFile => "theme-set";

        public OmarchySnapshot? Current() => snapshot;

        public HookInstall InstallHook(string line, string marker)
        {
            Hooked = line;
            return HookInstall.Created;
        }
    }

    [Fact]
    public void Without_a_current_theme_mocha_is_active() =>
        Assert.Same(BuiltInThemes.CatppuccinMocha, new ManageThemesHandler(new FakeStore(), new FakeOmarchy(null)).Active());

    [Fact]
    public void An_unknown_current_theme_falls_back_to_mocha() =>
        Assert.Same(
            BuiltInThemes.CatppuccinMocha,
            new ManageThemesHandler(new FakeStore { Current = "gone" }, new FakeOmarchy(null)).Active());

    [Fact]
    public void Set_saves_the_canonical_name()
    {
        var store = new FakeStore();
        var result = new ManageThemesHandler(store, new FakeOmarchy(null)).Set("Tokyo Night");

        Assert.True(result.Succeeded);
        Assert.Equal("tokyo-night", store.Current);
    }

    [Fact]
    public void Set_refuses_an_unknown_theme_and_keeps_the_current_one()
    {
        var store = new FakeStore { Current = "nord" };
        var result = new ManageThemesHandler(store, new FakeOmarchy(null)).Set("nope");

        Assert.False(result.Succeeded);
        Assert.Contains("fleet theme list", result.Error, StringComparison.Ordinal);
        Assert.Equal("nord", store.Current);
    }

    [Fact]
    public void A_custom_theme_is_listed_and_shadows_a_built_in_of_the_same_name()
    {
        var store = new FakeStore();
        store.SaveCustom(BuiltInThemes.CatppuccinMocha with { Name = "nord", Title = "My Nord" });
        var handler = new ManageThemesHandler(store, new FakeOmarchy(null));

        Assert.Equal(BuiltInThemes.All.Count, handler.List().Count);
        Assert.Equal("My Nord", Assert.Single(handler.List(), t => t.Name == "nord").Title);
        Assert.True(handler.Set("nord").Succeeded);
        Assert.Equal("My Nord", handler.Active().Title);
    }

    [Fact]
    public void Sync_maps_a_known_omarchy_theme_to_the_built_in()
    {
        var store = new FakeStore();
        var omarchy = new FakeOmarchy(new OmarchySnapshot("gruvbox", null, null, false));
        var result = new ManageThemesHandler(store, omarchy).SyncOmarchy();

        Assert.Equal("gruvbox-dark", result.Value.Name);
        Assert.Equal("gruvbox-dark", store.Current);
        Assert.Empty(store.Saved);
    }

    [Fact]
    public void Sync_saves_a_derived_theme_as_the_omarchy_custom_theme()
    {
        var store = new FakeStore();
        var snapshot = new OmarchySnapshot("matte-black", "background = \"#121212\"\nforeground = \"#bebebe\"", null, false);
        var handler = new ManageThemesHandler(store, new FakeOmarchy(snapshot));

        Assert.True(handler.SyncOmarchy().Succeeded);
        Assert.Equal(OmarchyTheme.CustomName, store.Current);
        Assert.Equal("#121212", handler.Active().Base);
    }

    [Fact]
    public void Sync_without_omarchy_fails() =>
        Assert.False(new ManageThemesHandler(new FakeStore(), new FakeOmarchy(null)).SyncOmarchy().Succeeded);

    [Fact]
    public void Install_hooks_omarchy_and_syncs_at_once()
    {
        var omarchy = new FakeOmarchy(new OmarchySnapshot("nord", null, null, false));
        var setup = new ManageThemesHandler(new FakeStore(), omarchy).InstallOmarchy("/opt/my fleet/fleet");

        Assert.Equal("'/opt/my fleet/fleet' theme sync >/dev/null 2>&1 || true", omarchy.Hooked);
        Assert.Equal(HookInstall.Created, setup.Hook);
        Assert.Equal("nord", setup.Theme?.Name);
    }

    [Fact]
    public void The_hook_line_quotes_a_path_with_a_single_quote() =>
        Assert.StartsWith("'/a'\\''b' theme sync", ManageThemesHandler.HookLine("/a'b"), StringComparison.Ordinal);

    [Fact]
    public void Saving_writes_the_normalized_name_inheriting_the_base_with_only_the_changes()
    {
        var store = new FakeStore();
        var nord = BuiltInThemes.Find("nord")!;
        var handler = new ManageThemesHandler(store, new FakeOmarchy(null));

        var result = handler.SaveCustom("My Theme!", "My Theme", "Nord", nord with { Blue = "#010203" });

        Assert.True(result.Succeeded);
        Assert.Equal("my-theme", result.Value.Name);
        Assert.Equal("#010203", result.Value.Blue);
        Assert.Equal(nord.Base, result.Value.Base);
        var values = ThemeToml.Parse(store.Texts["my-theme"]);
        Assert.Equal("nord", values["inherits"]);
        Assert.Equal("#010203", values["blue"]);
        Assert.DoesNotContain("base", values.Keys);
        Assert.True(handler.IsCustom(result.Value));
    }

    [Theory]
    [InlineData("  !! ")]
    [InlineData("")]
    [InlineData("Tokyo Night")]
    [InlineData("nord")]
    public void Saving_refuses_an_empty_or_built_in_name(string name)
    {
        var store = new FakeStore();
        var result = new ManageThemesHandler(store, new FakeOmarchy(null))
            .SaveCustom(name, "x", "nord", BuiltInThemes.CatppuccinMocha);

        Assert.False(result.Succeeded);
        Assert.Empty(store.Texts);
    }

    [Fact]
    public void Saving_refuses_an_unknown_base_or_the_theme_itself_as_base()
    {
        var store = new FakeStore();
        var handler = new ManageThemesHandler(store, new FakeOmarchy(null));
        handler.SaveCustom("mine", "Mine", "nord", BuiltInThemes.Find("nord")!);

        Assert.False(handler.SaveCustom("other", "Other", "nope", BuiltInThemes.CatppuccinMocha).Succeeded);
        Assert.False(handler.SaveCustom("Mine", "Mine", "mine", BuiltInThemes.CatppuccinMocha).Succeeded);
        Assert.Equal(["mine"], store.Texts.Keys);
    }

    [Fact]
    public void A_custom_theme_can_be_the_base_of_another()
    {
        var store = new FakeStore();
        var handler = new ManageThemesHandler(store, new FakeOmarchy(null));
        var parent = handler.SaveCustom("parent", "Parent", "nord", BuiltInThemes.Find("nord")! with { Blue = "#010203" }).Value;

        var child = handler.SaveCustom("child", "Child", "parent", parent with { Red = "#040506" });

        Assert.Equal("parent", ThemeToml.Parse(store.Texts["child"])["inherits"]);
        Assert.DoesNotContain("blue", ThemeToml.Parse(store.Texts["child"]).Keys);
        Assert.Equal("#010203", child.Value.Blue);
        Assert.Equal("#040506", child.Value.Red);
    }

    [Fact]
    public void A_blank_title_falls_back_to_the_name()
    {
        var store = new FakeStore();
        var result = new ManageThemesHandler(store, new FakeOmarchy(null))
            .SaveCustom("My Theme", " ", "nord", BuiltInThemes.Find("nord")!);

        Assert.Equal("My Theme", result.Value.Title);
    }

    [Fact]
    public void Saving_sets_light_from_the_resulting_base_color()
    {
        var store = new FakeStore();
        var mocha = BuiltInThemes.CatppuccinMocha;

        var result = new ManageThemesHandler(store, new FakeOmarchy(null))
            .SaveCustom("bright", "Bright", "catppuccin-mocha", mocha with { Base = "#fafafa" });

        Assert.True(result.Value.Light);
    }

    [Fact]
    public void Deleting_the_active_custom_theme_removes_it_and_falls_back_to_mocha()
    {
        var store = new FakeStore();
        var handler = new ManageThemesHandler(store, new FakeOmarchy(null));
        handler.SaveCustom("mine", "Mine", "nord", BuiltInThemes.Find("nord")!);
        handler.Set("mine");

        var result = handler.DeleteCustom("Mine");

        Assert.True(result.Succeeded);
        Assert.Same(BuiltInThemes.CatppuccinMocha, result.Value);
        Assert.Equal(BuiltInThemes.CatppuccinMocha.Name, store.Current);
        Assert.Empty(store.Saved);
    }

    [Fact]
    public void Deleting_an_inactive_custom_theme_keeps_the_current_one()
    {
        var store = new FakeStore();
        var handler = new ManageThemesHandler(store, new FakeOmarchy(null));
        handler.SaveCustom("mine", "Mine", "nord", BuiltInThemes.Find("nord")!);
        handler.Set("dracula");

        Assert.Equal("dracula", handler.DeleteCustom("mine").Value.Name);
        Assert.Equal("dracula", store.Current);
    }

    [Fact]
    public void Deleting_refuses_a_built_in_or_unknown_theme()
    {
        var store = new FakeStore { Current = "nord" };
        var handler = new ManageThemesHandler(store, new FakeOmarchy(null));

        Assert.Contains("built-in", handler.DeleteCustom("nord").Error, StringComparison.Ordinal);
        Assert.False(handler.DeleteCustom("nope").Succeeded);
        Assert.Equal("nord", store.Current);
    }

    [Fact]
    public void Saving_refuses_the_name_theme_sync_owns_but_deleting_it_still_works()
    {
        var store = new FakeStore();
        var handler = new ManageThemesHandler(store, new FakeOmarchy(null));

        var result = handler.SaveCustom("Omarchy", "Mine", "nord", BuiltInThemes.Find("nord")!);

        Assert.False(result.Succeeded);
        Assert.Contains("theme sync", result.Error, StringComparison.Ordinal);
        Assert.Empty(store.Texts);

        store.SaveCustom(BuiltInThemes.Find("nord")! with { Name = OmarchyTheme.CustomName });
        Assert.True(handler.DeleteCustom(OmarchyTheme.CustomName).Succeeded);
        Assert.Empty(store.Saved);
    }

    [Fact]
    public void The_base_of_a_custom_theme_is_what_it_inherits()
    {
        var store = new FakeStore();
        var handler = new ManageThemesHandler(store, new FakeOmarchy(null));

        handler.SaveCustom("parent", "Parent", "nord", BuiltInThemes.Find("nord")!);
        handler.SaveCustom("child", "Child", "Parent", BuiltInThemes.Find("nord")!);

        Assert.Equal("nord", handler.BaseOf("parent"));
        Assert.Equal("parent", handler.BaseOf("Child"));
    }

    [Fact]
    public void A_custom_theme_without_a_known_base_is_based_on_mocha()
    {
        var store = new FakeStore();
        var handler = new ManageThemesHandler(store, new FakeOmarchy(null));

        store.SaveCustom(BuiltInThemes.Find("nord")! with { Name = OmarchyTheme.CustomName });
        store.Texts["orphan"] = "inherits = \"gone\"\n";

        Assert.Equal(BuiltInThemes.DefaultName, handler.BaseOf(OmarchyTheme.CustomName));
        Assert.Equal(BuiltInThemes.DefaultName, handler.BaseOf("orphan"));
    }
}
