using Fleet.Shared.Themes;
using Fleet.Ui;
using Fleet.Ui.Constants;

namespace Fleet.Tests.Ui;

public sealed class FleetThemeEditorTests
{
    private static readonly ThemePalette Nord = BuiltInThemes.Find("nord")!;

    [Fact]
    public void It_lists_thirty_colors_fourteen_roles_then_eight_ansi_then_eight_brights()
    {
        var colors = FleetThemeEditor.Colors;

        Assert.Equal(30, colors.Count);
        Assert.Equal(ThemePalette.RoleNames, colors.Take(14).Select(c => c.Key));
        Assert.All(colors.Take(14), c => Assert.Equal(FleetThemeEditor.Roles, c.Group));
        Assert.Equal(Enumerable.Range(0, 8).Select(i => $"color{i}"), colors.Skip(14).Take(8).Select(c => c.Key));
        Assert.All(colors.Skip(14).Take(8), c => Assert.Equal(FleetThemeEditor.Ansi, c.Group));
        Assert.Equal(Enumerable.Range(8, 8).Select(i => $"color{i}"), colors.Skip(22).Select(c => c.Key));
        Assert.All(colors.Skip(22), c => Assert.Equal(FleetThemeEditor.Brights, c.Group));
    }

    [Fact]
    public void Of_reads_roles_ansi_and_brights_from_a_palette()
    {
        Assert.Equal(Nord.Base, FleetThemeEditor.Of(Nord, "base"));
        Assert.Equal(Nord.Ansi[1], FleetThemeEditor.Of(Nord, "color1"));
        Assert.Equal(Nord.Brights[7], FleetThemeEditor.Of(Nord, "color15"));
    }

    [Theory]
    [InlineData("cursor")]
    [InlineData("color3")]
    [InlineData("color12")]
    public void With_changes_only_the_chosen_color(string key)
    {
        var edited = FleetThemeEditor.With(Nord, key, "#010203");

        Assert.All(FleetThemeEditor.Colors, c =>
            Assert.Equal(c.Key == key ? "#010203" : FleetThemeEditor.Of(Nord, c.Key), FleetThemeEditor.Of(edited, c.Key)));
    }

    [Fact]
    public void With_leaves_the_original_palette_alone()
    {
        _ = FleetThemeEditor.With(Nord, "color0", "#010203");

        Assert.Equal(BuiltInThemes.Find("nord")!.Ansi[0], Nord.Ansi[0]);
        Assert.NotEqual("#010203", Nord.Ansi[0]);
    }

    [Fact]
    public void Each_row_shows_a_swatch_of_the_color_and_its_hex_value()
    {
        var rows = FleetThemeEditor.Rows(Nord);

        Assert.Equal(30, rows.Count);
        Assert.Contains(rows[2].Spans, s => s.Tone == FleetTones.Swatch(Nord.Base));
        Assert.Contains(Nord.Base, rows[2].Text, StringComparison.Ordinal);
        Assert.Contains("base", rows[2].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_rows_are_grouped_under_a_header_per_group()
    {
        var headers = FleetThemeEditor.Headers();

        Assert.Equal([0, 14, 22], headers.Keys.Order());
        Assert.Equal(
            [FleetThemeEditor.Roles, FleetThemeEditor.Ansi, FleetThemeEditor.Brights],
            headers.OrderBy(h => h.Key).Select(h => h.Value.Text.Trim()));
    }

    [Theory]
    [InlineData("My Theme!", "my-theme")]
    [InlineData("  Paper  ", "paper")]
    public void A_new_name_is_normalized(string typed, string expected)
    {
        var name = FleetThemeEditor.NewName(typed);

        Assert.True(name.Succeeded);
        Assert.Equal(expected, name.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  !! ")]
    [InlineData("Nord")]
    [InlineData("Catppuccin Mocha")]
    [InlineData("omarchy")]
    public void A_new_name_is_rejected_when_empty_or_taken_by_a_built_in(string typed) =>
        Assert.False(FleetThemeEditor.NewName(typed).Succeeded);

    [Fact]
    public void A_palette_is_changed_only_when_a_color_differs()
    {
        Assert.False(FleetThemeEditor.Changed(Nord, Nord with { Title = "Other" }));
        Assert.True(FleetThemeEditor.Changed(Nord, FleetThemeEditor.With(Nord, "color9", "#010203")));
    }

    [Fact]
    public void A_copy_of_the_synced_omarchy_theme_does_not_inherit_from_it()
    {
        Assert.Equal(BuiltInThemes.DefaultName, FleetThemeEditor.Inheritable(OmarchyTheme.CustomName));
        Assert.Equal("nord", FleetThemeEditor.Inheritable("nord"));
        Assert.Equal("paper", FleetThemeEditor.Inheritable("paper"));
    }

    [Theory]
    [InlineData("paper", false)]
    [InlineData("omarchy", true)]
    [InlineData("nord", true)]
    public void A_custom_theme_saved_under_a_name_it_cannot_keep_is_saved_as_a_new_one(string name, bool savesAs) =>
        Assert.Equal(savesAs, FleetThemeEditor.SavesAs(name));
}
