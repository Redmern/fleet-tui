using Fleet.Shared.Themes;
using Fleet.Ui;

namespace Fleet.Tests.Ui;

public sealed class FleetThemeTabsTests
{
    private static readonly ThemePalette Mocha = BuiltInThemes.CatppuccinMocha;

    private static readonly ThemePalette Latte = BuiltInThemes.Find("catppuccin-latte")!;

    private static readonly ThemePalette Tokyo = BuiltInThemes.Find("tokyo-night")!;

    private static readonly ThemePalette Omarchy = Mocha with { Name = "omarchy", Title = "Omarchy" };

    private static readonly ThemePalette Paper = Latte with { Name = "paper", Title = "Paper" };

    private static readonly ThemePalette ShadowedTokyo = Tokyo with { Title = "My Tokyo" };

    private static FleetThemeTabs Tabs(string active, params ThemePalette[] custom) =>
        FleetThemeTabs.For(
            [.. BuiltInThemes.All.Where(b => custom.All(c => c.Name != b.Name)), .. custom],
            t => custom.Any(c => c.Name == t.Name),
            active);

    private static IEnumerable<string> Names(FleetThemeTabs tabs, int tab) =>
        tabs.Tabs[tab].Entries.Select(e => e.Detail.Replace(FleetThemeTabs.ActiveMark, string.Empty, StringComparison.Ordinal));

    [Fact]
    public void The_tabs_are_dark_light_and_custom_in_that_order()
    {
        Assert.Equal(["Dark", "Light", "Custom"], Tabs(Mocha.Name).Tabs.Select(t => t.Title));
    }

    [Fact]
    public void Dark_lists_only_dark_built_ins_and_light_only_light_ones()
    {
        var tabs = Tabs(Mocha.Name);

        Assert.Equal(BuiltInThemes.All.Where(t => !t.Light).Select(t => t.Name), Names(tabs, FleetThemeTabs.Dark));
        Assert.Equal(BuiltInThemes.All.Where(t => t.Light).Select(t => t.Name), Names(tabs, FleetThemeTabs.Light));
        Assert.Empty(Names(tabs, FleetThemeTabs.Dark).Intersect(Names(tabs, FleetThemeTabs.Light)));
    }

    [Fact]
    public void Custom_lists_every_custom_theme_light_or_dark_and_nothing_else()
    {
        var tabs = Tabs(Mocha.Name, Omarchy, Paper);

        Assert.Equal(["omarchy", "paper"], Names(tabs, FleetThemeTabs.Custom));
        Assert.DoesNotContain("paper", Names(tabs, FleetThemeTabs.Light));
        Assert.DoesNotContain("omarchy", Names(tabs, FleetThemeTabs.Dark));
    }

    [Fact]
    public void A_custom_theme_shadowing_a_built_in_shows_only_under_custom()
    {
        var tabs = Tabs(Mocha.Name, ShadowedTokyo);

        Assert.Equal(["tokyo-night"], Names(tabs, FleetThemeTabs.Custom));
        Assert.DoesNotContain("tokyo-night", Names(tabs, FleetThemeTabs.Dark));
        Assert.Equal("My Tokyo", tabs.Tabs[FleetThemeTabs.Custom].Entries.Single().Label);
    }

    [Fact]
    public void An_empty_custom_tab_has_no_entries()
    {
        Assert.Empty(Tabs(Mocha.Name).Tabs[FleetThemeTabs.Custom].Entries);
    }

    [Theory]
    [InlineData("catppuccin-mocha", FleetThemeTabs.Dark)]
    [InlineData("catppuccin-latte", FleetThemeTabs.Light)]
    [InlineData("paper", FleetThemeTabs.Custom)]
    public void It_opens_on_the_tab_holding_the_active_theme_with_it_selected(string active, int tab)
    {
        var tabs = Tabs(active, Paper);

        Assert.Equal(tab, tabs.StartTab);
        Assert.Equal(active, tabs.Pick(tabs.StartTab, tabs.StartIndex).Name);
    }

    [Fact]
    public void Only_the_active_theme_is_marked_active()
    {
        var tabs = Tabs(Latte.Name);

        var marked = tabs.Tabs.SelectMany(t => t.Entries).Where(e => e.Detail.EndsWith(FleetThemeTabs.ActiveMark, StringComparison.Ordinal));

        Assert.Equal($"{Latte.Name}{FleetThemeTabs.ActiveMark}", Assert.Single(marked).Detail);
    }

    [Fact]
    public void An_unknown_active_theme_opens_on_the_first_dark_theme()
    {
        var tabs = Tabs("gone");

        Assert.Equal((FleetThemeTabs.Dark, 0), (tabs.StartTab, tabs.StartIndex));
    }

    [Fact]
    public void Pick_returns_the_theme_behind_a_tab_entry()
    {
        var tabs = Tabs(Mocha.Name, Paper);

        Assert.Equal(Paper, tabs.Pick(FleetThemeTabs.Custom, 0));
        Assert.Equal(Latte, tabs.Pick(FleetThemeTabs.Light, 0));
    }

    [Fact]
    public void With_new_the_custom_tab_ends_with_a_new_custom_theme_entry()
    {
        var tabs = FleetThemeTabs.For([.. BuiltInThemes.All, Paper], t => t == Paper, Mocha.Name, withNew: true);
        var custom = tabs.Tabs[FleetThemeTabs.Custom].Entries;

        Assert.Equal(["Paper", FleetThemeTabs.NewLabel], custom.Select(e => e.Label));
        Assert.True(tabs.IsNew(FleetThemeTabs.Custom, 1));
        Assert.False(tabs.IsNew(FleetThemeTabs.Custom, 0));
        Assert.False(tabs.IsNew(FleetThemeTabs.Dark, 1));
        Assert.Equal(Paper, tabs.Pick(FleetThemeTabs.Custom, 0));
    }

    [Fact]
    public void Without_custom_themes_the_new_entry_is_the_only_custom_entry()
    {
        var tabs = FleetThemeTabs.For(BuiltInThemes.All, _ => false, Mocha.Name, withNew: true);

        Assert.Equal(FleetThemeTabs.NewLabel, Assert.Single(tabs.Tabs[FleetThemeTabs.Custom].Entries).Label);
        Assert.True(tabs.IsNew(FleetThemeTabs.Custom, 0));
        Assert.Equal((FleetThemeTabs.Dark, 0), (tabs.StartTab, tabs.StartIndex));
    }

    [Fact]
    public void It_can_open_on_another_theme_than_the_active_one()
    {
        var tabs = FleetThemeTabs.For([.. BuiltInThemes.All, Paper], t => t == Paper, Mocha.Name, select: Paper.Name);

        Assert.Equal(Paper, tabs.Pick(tabs.StartTab, tabs.StartIndex));
        Assert.EndsWith(FleetThemeTabs.ActiveMark, tabs.Tabs[FleetThemeTabs.Dark].Entries[0].Detail, StringComparison.Ordinal);
    }
}
