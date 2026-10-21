using Fleet.Shared.Settings.Enums;
using Fleet.Ui;
using Fleet.Ui.Constants;
using Fleet.Ui.Models;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.Views;

namespace Fleet.Tests.Ui;

public sealed class FleetPillButtonTests : IDisposable
{
    public FleetPillButtonTests() => FleetButtonHints.Reset();

    public void Dispose() => FleetButtonHints.Reset();

    private static FleetPillButton Pill(string key = "c", Action? activate = null) =>
        new(FleetIcons.Create, "Create agent", key, activate ?? (() => { }));

    [Fact]
    public void A_pill_reads_edge_icon_label_key_chip_edge()
    {
        var row = Pill().Row(ButtonHints.None);

        Assert.Equal(
            $"{FleetGlyphs.PillLeft} {FleetIcons.Create} Create agent  c {FleetGlyphs.PillRight}",
            row.Text);
    }

    [Fact]
    public void The_key_chip_is_drawn_in_the_key_tone_and_the_edges_in_the_edge_tone()
    {
        var spans = Pill().Row(ButtonHints.None).Spans;

        Assert.Equal(FleetTones.ChipEdge, spans[0].Tone);
        Assert.Equal(FleetTones.ChipLabel, spans[1].Tone);
        Assert.Equal(new FleetSpan(" c ", FleetTones.ChipKey), spans[2]);
        Assert.Equal(FleetTones.ChipEdge, spans[^1].Tone);
    }

    [Fact]
    public void A_pill_without_a_key_has_no_key_chip()
    {
        var row = Pill(key: string.Empty).Row(ButtonHints.None);

        Assert.DoesNotContain(row.Spans, s => s.Tone == FleetTones.ChipKey);
    }

    [Fact]
    public void A_named_key_is_shown_the_way_the_action_bar_shows_it()
    {
        var row = Pill(key: "Esc").Row(ButtonHints.None);

        Assert.Contains(row.Spans, s => s.Text == " esc " && s.Tone == FleetTones.ChipKey);
    }

    [Fact]
    public void A_pill_can_take_its_own_label_tone()
    {
        var pill = new FleetPillButton(FleetIcons.Close, "Cancel", "Esc", () => { }, FleetTones.Muted);

        Assert.Equal(FleetTones.Muted, pill.Row(ButtonHints.None).Spans[1].Tone);
    }

    [Theory]
    [InlineData(ButtonHints.None)]
    [InlineData(ButtonHints.Tooltips)]
    [InlineData(ButtonHints.Text)]
    public void The_label_is_shown_in_every_button_hints_mode(ButtonHints hints) =>
        Assert.Contains("Create agent", Pill().Row(hints).Text);

    [Fact]
    public void An_icon_only_pill_follows_the_button_hints_face()
    {
        var pill = new FleetPillButton(FleetIcons.Back, string.Empty, "b", () => { });

        Assert.Contains($" {FleetIcons.Back} back ", pill.Row(ButtonHints.Text).Text);
        Assert.Contains($" {FleetIcons.Back} ", pill.Row(ButtonHints.Tooltips).Text);
        Assert.DoesNotContain("back", pill.Row(ButtonHints.Tooltips).Text);
    }

    [Fact]
    public void A_labelled_pill_does_not_repeat_its_icon_name_in_text_mode() =>
        Assert.DoesNotContain("create", Pill().Row(ButtonHints.Text).Text);

    [Fact]
    public void A_pill_answers_its_own_key_only()
    {
        Assert.True(Pill().Answers(Key.C));
        Assert.False(Pill().Answers(Key.C.WithShift));
        Assert.False(Pill().Answers(Key.D));
        Assert.False(Pill(key: string.Empty).Answers(Key.C));
    }

    [Theory]
    [InlineData("j")]
    [InlineData("k")]
    [InlineData("g")]
    [InlineData("G")]
    [InlineData("Ctrl+D")]
    [InlineData("Backspace")]
    [InlineData("Enter")]
    public void A_key_the_list_already_uses_clashes(string key) =>
        Assert.True(Pill(key).Clashes(Keymap.Default));

    [Theory]
    [InlineData("c")]
    [InlineData("a")]
    [InlineData("d")]
    [InlineData("Esc")]
    public void A_mnemonic_the_list_does_not_use_is_free(string key) =>
        Assert.False(Pill(key).Clashes(Keymap.Default));

    [Fact]
    public void Pressing_a_mnemonic_while_the_list_has_focus_activates_the_pill()
    {
        using var app = Application.Create().Init("ansi");
        var activated = 0;
        var (window, list) = Open(app, [Pill(activate: () => activated++)]);

        app.Keyboard.RaiseKeyDownEvent(Key.C);

        Assert.Equal(1, activated);
        window.Dispose();
    }

    [Fact]
    public void Other_keys_reach_the_list_as_before()
    {
        using var app = Application.Create().Init("ansi");
        var activated = 0;
        var (window, list) = Open(app, [Pill(activate: () => activated++)]);

        app.Keyboard.RaiseKeyDownEvent(Key.J);

        Assert.Equal(0, activated);
        Assert.Equal(1, FleetRows.Selected(list));
        window.Dispose();
    }

    [Fact]
    public void Typing_the_mnemonic_into_a_text_field_inserts_it_as_text()
    {
        using var app = Application.Create().Init("ansi");
        var activated = 0;
        var (window, _) = Open(app, [Pill(activate: () => activated++)]);
        var field = new TextField { Y = 4, Width = 10 };
        window.Add(field);
        field.SetFocus();

        app.Keyboard.RaiseKeyDownEvent(Key.C);

        Assert.Equal(0, activated);
        Assert.Equal("c", field.Text);
        window.Dispose();
    }

    private static (Window Window, ListView List) Open(IApplication app, IReadOnlyList<FleetPillButton> pills)
    {
        var window = new Window { Width = 40, Height = 10 };
        var list = FleetTheme.Rows(0, 0, 3);
        FleetRows.Fill(list, [FleetRow.Plain("one"), FleetRow.Plain("two"), .. pills.Select(p => p.Row())]);
        FleetKeys.ApplyMotions(list, Keymap.Default);
        FleetPillButton.Bind(list, pills);
        window.Add(list);
        app.Begin(window);
        list.SetFocus();

        return (window, list);
    }
}
