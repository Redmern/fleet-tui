using Fleet.Shared.Keymap;
using Fleet.Shared.Keymap.Enums;
using Fleet.Ui;

namespace Fleet.Tests.Ui;

public class FleetKeyTextTests
{
    [Theory]
    [InlineData("Ctrl+Space", "ctrl+space")]
    [InlineData("Ctrl+S", "ctrl+s")]
    [InlineData("Ctrl+D", "ctrl+d")]
    [InlineData("Ctrl+Shift+P", "ctrl+shift+p")]
    [InlineData("Space", "space")]
    [InlineData("Esc", "esc")]
    [InlineData("Enter", "enter")]
    [InlineData("F5", "f5")]
    public void Modifiers_and_named_keys_read_lowercase(string input, string expected)
        => Assert.Equal(expected, FleetKeyText.Display(input));

    [Theory]
    [InlineData("q", "q")]
    [InlineData("G", "G")]
    [InlineData("j", "j")]
    public void A_bare_letter_keeps_its_case_because_shift_is_what_capitalises_it(
        string input, string expected)
        => Assert.Equal(expected, FleetKeyText.Display(input));

    [Fact]
    public void Jump_first_and_jump_last_stay_distinguishable()
    {
        var first = FleetKeyText.Display(Keymap.Default.TextFor(FleetAction.MoveFirst));
        var last = FleetKeyText.Display(Keymap.Default.TextFor(FleetAction.MoveLast));

        Assert.Equal("g", first);
        Assert.Equal("G", last);
        Assert.NotEqual(first, last);
    }

    [Theory]
    [InlineData("CursorDown", "down")]
    [InlineData("CursorUp", "up")]
    [InlineData("PageDown", "pgdn")]
    public void Long_names_get_a_short_alias(string input, string expected)
        => Assert.Equal(expected, FleetKeyText.Display(input));

    [Fact]
    public void Empty_input_renders_empty()
    {
        Assert.Equal(string.Empty, FleetKeyText.Display(string.Empty));
        Assert.Equal(string.Empty, FleetKeyText.Display("   "));
    }

    [Fact]
    public void Display_never_changes_what_is_stored()
    {
        var keymap = Keymap.Default;

        Assert.Equal(KeymapDefaults.Prefix, keymap.PrefixText);
        Assert.Equal("ctrl+space", keymap.PrefixDisplay);
    }

    [Fact]
    public void The_prefix_hint_reads_lowercase()
        => Assert.Equal("ctrl+space space menu", FleetHintText.Menu(Keymap.Default));
}
