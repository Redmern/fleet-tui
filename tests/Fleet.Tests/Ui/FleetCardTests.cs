using Fleet.Shared.Keymap.Enums;
using Fleet.Shared.Keymap.Models;
using Fleet.Shared.Settings.Enums;
using Fleet.Ui;
using Fleet.Ui.Constants;
using Terminal.Gui.Input;

namespace Fleet.Tests.Ui;

public class FleetCardTests
{
    private static IReadOnlyList<FleetCardField> Fields(List<string>? ran = null) =>
    [
        new(FleetIcons.Repo, "Repo", "fleet", () => ran?.Add("repo")),
        new(FleetIcons.Branch, "Branch", "feat/x", () => ran?.Add("branch")),
        new(FleetIcons.Base, "Base", "main", () => ran?.Add("base")),
    ];

    private static IReadOnlyList<FleetPillButton> Pills(List<string>? ran = null) =>
        FleetCard.Pills(FleetIcons.Create, "Create agent", "c", () => ran?.Add("create"), () => ran?.Add("cancel"));

    [Fact]
    public void A_field_row_leads_with_its_icon_then_label_and_inline_value()
    {
        var row = FleetCard.FieldRow(Fields()[1], "b");

        Assert.Equal(FleetIcons.Branch, row.Spans[0].Text);
        Assert.Equal(FleetTones.Icon, row.Spans[0].Tone);
        Assert.Equal($"{FleetIcons.Branch} Branch:  feat/x", string.Concat(row.Spans.Select(s => s.Text)));
    }

    [Fact]
    public void A_field_row_shows_its_jump_key_as_a_chip()
    {
        var row = FleetCard.FieldRow(Fields()[1], "b");

        Assert.NotNull(row.Trailing);
        Assert.Contains(row.Trailing!, s => s.Tone == FleetTones.ChipKey && s.Text.Contains('b'));
    }

    [Fact]
    public void The_card_has_a_submit_pill_and_a_cancel_pill_on_esc()
    {
        var pills = Pills();

        Assert.Equal(("Create agent", FleetIcons.Create, "c"), (pills[0].Label, pills[0].Icon, pills[0].Mnemonic));
        Assert.Equal(("Cancel", FleetIcons.Close, "Esc"), (pills[1].Label, pills[1].Icon, pills[1].Mnemonic));
    }

    [Fact]
    public void Each_pill_shows_a_key_chip()
    {
        Assert.All(Pills(), p => Assert.Contains(p.Row(ButtonHints.None).Spans, s => s.Tone == FleetTones.ChipKey));
    }

    [Fact]
    public void Field_jump_keys_avoid_the_pill_keys_and_the_motions()
    {
        var keys = FleetCard.FieldKeys(
            [new(FleetIcons.Create, "Copy", "x", () => { }), new(FleetIcons.Repo, "Jump", "y", () => { })],
            Pills(),
            Keymap.Default);

        Assert.DoesNotContain("c", keys);
        Assert.DoesNotContain("j", keys);
        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    [Fact]
    public void Each_field_gets_a_jump_key()
    {
        Assert.Equal(["r", "b", "a"], FleetCard.FieldKeys(Fields(), Pills(), Keymap.Default));
    }

    [Fact]
    public void A_field_jump_key_lands_on_its_field()
    {
        var keys = FleetCard.FieldKeys(Fields(), Pills(), Keymap.Default);

        Assert.Equal(2, FleetCard.Jump(Key.A, keys, Pills()));
    }

    [Fact]
    public void A_pill_key_lands_on_its_pill_after_the_fields()
    {
        var keys = FleetCard.FieldKeys(Fields(), Pills(), Keymap.Default);

        Assert.Equal(3, FleetCard.Jump(Key.C, keys, Pills()));
    }

    [Fact]
    public void A_key_no_field_or_pill_answers_does_not_jump()
    {
        var keys = FleetCard.FieldKeys(Fields(), Pills(), Keymap.Default);

        Assert.Null(FleetCard.Jump(Key.J, keys, Pills()));
        Assert.Null(FleetCard.Jump(Key.Z, keys, Pills()));
    }

    [Fact]
    public void A_field_key_rebound_to_a_motion_moves_to_another_letter()
    {
        var keymap = new Keymap(KeymapConfig.Default.With(FleetAction.MoveDown, "r"));

        Assert.NotEqual("r", FleetCard.FieldKeys(Fields(), Pills(), keymap)[0]);
    }

    [Fact]
    public void The_pills_run_submit_and_cancel()
    {
        var ran = new List<string>();
        var pills = Pills(ran);

        pills[0].Activate();
        pills[1].Activate();

        Assert.Equal(["create", "cancel"], ran);
    }

    [Fact]
    public void The_footer_offers_ctrl_enter_to_submit_and_backspace_to_go_back()
    {
        var keys = FleetCard.Footer(() => { }, () => { }, () => { }).Select(i => i.Key).ToList();

        Assert.Contains("enter", keys);
        Assert.Contains("ctrl+enter", keys);
        Assert.Contains("bksp", keys);
    }
}
