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
}
