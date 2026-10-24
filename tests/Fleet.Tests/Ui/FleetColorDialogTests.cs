using Fleet.Ui;
using Terminal.Gui.Input;

namespace Fleet.Tests.Ui;

public sealed class FleetColorDialogTests
{
    [Fact]
    public void Channels_split_a_hex_color_into_red_green_and_blue() =>
        Assert.Equal((0x12, 0xab, 0xff), FleetColorDialog.Channels("#12ABff"));

    [Fact]
    public void Hex_joins_channels_into_a_lower_case_color() =>
        Assert.Equal("#0aff00", FleetColorDialog.Hex(10, 255, 0));

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(300, 255)]
    public void Hex_clamps_channels_into_a_byte(int value, int expected) =>
        Assert.Equal(FleetColorDialog.Hex(expected, expected, expected), FleetColorDialog.Hex(value, value, value));

    [Theory]
    [InlineData(FleetColorDialog.Red, 1, "#11203f")]
    [InlineData(FleetColorDialog.Green, -1, "#101f3f")]
    [InlineData(FleetColorDialog.Blue, 16, "#10204f")]
    public void Nudge_moves_one_channel_and_leaves_the_others(int channel, int delta, string expected) =>
        Assert.Equal(expected, FleetColorDialog.Nudge("#10203f", channel, delta));

    [Fact]
    public void Nudge_stops_at_the_ends_of_a_channel()
    {
        Assert.Equal("#00ff00", FleetColorDialog.Nudge("#08ff00", FleetColorDialog.Red, -FleetColorDialog.BigStep));
        Assert.Equal("#00ff00", FleetColorDialog.Nudge("#00f800", FleetColorDialog.Green, FleetColorDialog.BigStep));
    }

    [Fact]
    public void Arrows_step_by_one_and_shifted_arrows_or_page_keys_by_sixteen()
    {
        Assert.Equal(-1, FleetColorDialog.Delta(Key.CursorLeft));
        Assert.Equal(1, FleetColorDialog.Delta(Key.CursorRight));
        Assert.Equal(-16, FleetColorDialog.Delta(Key.CursorLeft.WithShift));
        Assert.Equal(16, FleetColorDialog.Delta(Key.CursorRight.WithShift));
        Assert.Equal(-16, FleetColorDialog.Delta(Key.PageDown));
        Assert.Equal(16, FleetColorDialog.Delta(Key.PageUp));
        Assert.Null(FleetColorDialog.Delta(Key.A));
    }

    [Theory]
    [InlineData("#ABCDEF", "#abcdef")]
    [InlineData(" 0x123456 ", "#123456")]
    [InlineData("12345", "#10203f")]
    [InlineData("#zzzzzz", "#10203f")]
    [InlineData("", "#10203f")]
    public void Typed_hex_replaces_the_color_only_when_it_is_valid(string typed, string expected) =>
        Assert.Equal(expected, FleetColorDialog.Typed("#10203f", typed));

    [Fact]
    public void A_slider_fills_in_proportion_to_its_value()
    {
        Assert.Equal(new string('░', 16), FleetColorDialog.Slider(0, 16));
        Assert.Equal(new string('█', 16), FleetColorDialog.Slider(255, 16));
        Assert.Equal(new string('█', 8) + new string('░', 8), FleetColorDialog.Slider(128, 16));
    }
}
