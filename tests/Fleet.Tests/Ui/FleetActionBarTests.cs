using System.Reflection;
using Fleet.Shared.Keymap.Enums;
using Fleet.Shared.Keymap.Models;
using Fleet.Shared.Settings.Enums;
using Fleet.Ui;
using Fleet.Ui.Constants;
using Fleet.Ui.Models;

namespace Fleet.Tests.Ui;

public sealed class FleetActionBarTests : IDisposable
{
    private static readonly string[] SectionIcons =
    [
        FleetIcons.Session,
        FleetIcons.Configure,
        FleetIcons.Maintenance,
        FleetIcons.Models,
        FleetIcons.Permissions,
    ];

    public FleetActionBarTests() => FleetButtonHints.Reset();

    public void Dispose() => FleetButtonHints.Reset();

    private static IReadOnlyList<(string Key, string Label, Action Run)> Bar(Action? second = null) =>
    [
        ("enter", FleetIcons.Select, () => { }),
        ("bksp", FleetIcons.Back, second ?? (() => { })),
    ];

    private static string Text(IReadOnlyList<FleetChip> chips) =>
        string.Concat(chips.SelectMany(c => c.Spans).Select(s => s.Text));

    [Fact]
    public void Every_icon_a_button_can_carry_has_a_name()
    {
        var icons = typeof(FleetIcons)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (f.Name, Icon: (string)f.GetRawConstantValue()!))
            .Where(f => !SectionIcons.Contains(f.Icon))
            .ToList();

        Assert.NotEmpty(icons);

        foreach (var (name, icon) in icons)
        {
            Assert.False(string.IsNullOrWhiteSpace(FleetIcons.Name(icon)), $"{name} has no name");
        }
    }

    [Theory]
    [InlineData(FleetAction.NewAgent, "new agent")]
    [InlineData(FleetAction.AddRepository, "add repo")]
    [InlineData(FleetAction.Refresh, "refresh")]
    public void The_action_icons_used_on_bars_have_names(FleetAction action, string name)
        => Assert.Equal(name, FleetIcons.Name(FleetIcons.For(action)!));

    [Fact]
    public void Toggles_are_named_for_the_state_they_show()
    {
        Assert.Equal("bell on", FleetIcons.Name(FleetIcons.BellOn));
        Assert.Equal("bell off", FleetIcons.Name(FleetIcons.BellOff));
        Assert.Equal("toasts on", FleetIcons.Name(FleetIcons.ToastsOn));
        Assert.Equal("toasts off", FleetIcons.Name(FleetIcons.ToastsOff));
        Assert.Equal("show", FleetIcons.Name(FleetIcons.Show));
        Assert.Equal("hide", FleetIcons.Name(FleetIcons.Hide));
    }

    [Fact]
    public void Nothing_that_is_not_an_icon_has_a_name()
        => Assert.Equal(string.Empty, FleetIcons.Name("select"));

    [Fact]
    public void Icon_only_chips_are_a_pill_with_a_key_and_the_icon()
    {
        var chips = FleetActionBar.Chips(Bar(), keys: true, pinned: null, ButtonHints.Tooltips);

        Assert.Equal(
            $"{FleetGlyphs.PillLeft} enter {FleetIcons.Select} {FleetGlyphs.PillRight} {FleetGlyphs.PillLeft} bksp {FleetIcons.Back} {FleetGlyphs.PillRight} ",
            Text(chips));
        Assert.Equal(FleetActionBar.Measure(Bar(), ButtonHints.Tooltips), FleetActionBar.Width(chips));
    }

    [Fact]
    public void Text_mode_adds_the_name_after_the_icon_and_widens_the_chips()
    {
        var chips = FleetActionBar.Chips(Bar(), keys: true, pinned: null, ButtonHints.Text);

        Assert.Equal(
            $"{FleetGlyphs.PillLeft} enter {FleetIcons.Select} select {FleetGlyphs.PillRight} {FleetGlyphs.PillLeft} bksp {FleetIcons.Back} back {FleetGlyphs.PillRight} ",
            Text(chips));
        Assert.Equal(
            FleetActionBar.Measure(Bar(), ButtonHints.Tooltips) + " select".Length + " back".Length,
            FleetActionBar.Measure(Bar(), ButtonHints.Text));
        Assert.Equal(FleetActionBar.Measure(Bar(), ButtonHints.Text), FleetActionBar.Width(chips));
    }

    [Fact]
    public void The_hit_ranges_cover_each_chip_and_the_gap_between_them_hits_nothing()
    {
        var chips = FleetActionBar.Chips(Bar(), keys: true, pinned: null, ButtonHints.Text);
        var first = chips[0];
        var second = chips[1];

        Assert.Equal(0, first.From);
        Assert.Equal(second.From - 2, first.To);
        Assert.Equal(FleetActionBar.Width(chips) - 1, second.To);

        Assert.Equal(-1, FleetActionBar.Hovered(chips, -1));
        Assert.Equal(0, FleetActionBar.Hovered(chips, first.From));
        Assert.Equal(0, FleetActionBar.Hovered(chips, first.To));
        Assert.Equal(-1, FleetActionBar.Hovered(chips, first.To + 1));
        Assert.Equal(1, FleetActionBar.Hovered(chips, second.From));
        Assert.Equal(1, FleetActionBar.Hovered(chips, second.To));
        Assert.Equal(-1, FleetActionBar.Hovered(chips, second.To + 1));
    }

    [Fact]
    public void Hit_ranges_follow_the_text_when_the_mode_changes()
    {
        var icons = FleetActionBar.Chips(Bar(), keys: true, pinned: null, ButtonHints.None);
        var text = FleetActionBar.Chips(Bar(), keys: true, pinned: null, ButtonHints.Text);

        Assert.Equal(-1, FleetActionBar.Hovered(icons, text[1].To));
        Assert.Equal(1, FleetActionBar.Hovered(text, text[1].To));
    }

    [Fact]
    public void A_chip_remembers_the_name_to_show_and_what_to_run()
    {
        var ran = 0;
        var chips = FleetActionBar.Chips(Bar(second: () => ran++), keys: false, pinned: null, ButtonHints.Tooltips);

        Assert.Equal(["select", "back"], chips.Select(c => c.Name));

        chips[1].Run();

        Assert.Equal(1, ran);
    }

    [Fact]
    public void A_tooltip_names_the_key_of_a_fleet_action_chip_even_while_the_keys_are_hidden()
    {
        var keymap = new Keymap(KeymapConfig.Default with
        {
            Bindings = new Dictionary<FleetAction, string> { [FleetAction.NewAgent] = "Ctrl+N" },
        });
        var chips = FleetActionBar.Chips(
            [
                (keymap.DisplayFor(FleetAction.NewAgent), FleetIcons.For(FleetAction.NewAgent)!, () => { }),
                ("enter", FleetIcons.Select, () => { }),
                (string.Empty, FleetIcons.Menu, () => { }),
            ],
            keys: false,
            pinned: null,
            ButtonHints.Tooltips,
            tips: [keymap.DisplayFor(FleetAction.NewAgent), string.Empty, string.Empty]);

        Assert.Equal(["new agent (ctrl+n)", "select", "menu"], chips.Select(c => c.Tip));
    }

    [Fact]
    public void A_chip_without_a_fleet_action_keeps_a_plain_tooltip()
    {
        var chips = FleetActionBar.Chips(Bar(), keys: true, pinned: null, ButtonHints.Tooltips);

        Assert.Equal(["select", "back"], chips.Select(c => c.Tip));
    }

    [Fact]
    public void A_tooltip_label_adds_nothing_without_a_key_or_a_name()
    {
        Assert.Equal("theme (T)", FleetToolTip.Label("theme", "T"));
        Assert.Equal("theme", FleetToolTip.Label("theme", string.Empty));
        Assert.Equal(string.Empty, FleetToolTip.Label(string.Empty, "T"));
    }

    [Fact]
    public void Hiding_the_keys_narrows_the_chips_and_keeps_the_pinned_one()
    {
        var chips = FleetActionBar.Chips(Bar(), keys: false, pinned: "bksp", ButtonHints.Tooltips);

        Assert.Equal(
            $"{FleetGlyphs.PillLeft} {FleetIcons.Select} {FleetGlyphs.PillRight} {FleetGlyphs.PillLeft} bksp {FleetIcons.Back} {FleetGlyphs.PillRight} ",
            Text(chips));
    }

    [Fact]
    public void The_face_of_a_button_only_names_it_in_text_mode()
    {
        Assert.Equal(FleetIcons.Close, FleetButtonHints.Face(FleetIcons.Close, ButtonHints.Tooltips));
        Assert.Equal(FleetIcons.Close, FleetButtonHints.Face(FleetIcons.Close, ButtonHints.None));
        Assert.Equal($"{FleetIcons.Close} close", FleetButtonHints.Face(FleetIcons.Close, ButtonHints.Text));
        Assert.Equal("select", FleetButtonHints.Face("select", ButtonHints.Text));
    }

    [Fact]
    public void Applying_button_hints_tells_the_chips_to_rebuild_once_per_change()
    {
        var changes = 0;
        FleetButtonHints.Changed += () => changes++;

        Assert.Equal(ButtonHints.Tooltips, FleetButtonHints.Mode);

        FleetButtonHints.Apply(ButtonHints.Tooltips);
        FleetButtonHints.Apply(ButtonHints.Text);
        FleetButtonHints.Apply(ButtonHints.Text);

        Assert.Equal(ButtonHints.Text, FleetButtonHints.Mode);
        Assert.Equal(1, changes);
        Assert.Equal(FleetActionBar.Measure(Bar(), ButtonHints.Text), FleetActionBar.Measure(Bar()));
    }

    [Fact]
    public void The_corners_show_the_name_next_to_the_icon_in_text_mode_and_keep_their_width_across_keys()
    {
        var help = string.Concat(FleetCorners.Help(keysShown: false, "?", ButtonHints.Text).Select(s => s.Text));
        var shown = string.Concat(FleetCorners.Help(keysShown: true, "?", ButtonHints.Text).Select(s => s.Text));
        var close = string.Concat(FleetCorners.Close(keysShown: false, ButtonHints.Text).Select(s => s.Text));
        var closeKeyed = string.Concat(FleetCorners.Close(keysShown: true, ButtonHints.Text).Select(s => s.Text));

        Assert.Equal($"{FleetGlyphs.PillLeft} ? {FleetIcons.Info} keybinds {FleetGlyphs.PillRight}", help);
        Assert.Equal(help.Length, shown.Length);
        Assert.Equal($"{FleetGlyphs.PillLeft} esc {FleetIcons.Close} close {FleetGlyphs.PillRight}", closeKeyed);
        Assert.Equal(closeKeyed.Length, close.Length);
    }
}
