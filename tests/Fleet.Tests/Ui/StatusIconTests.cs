using Fleet.Ui;
using Fleet.Ui.Constants;

namespace Fleet.Tests.Ui;

public sealed class StatusIconTests
{
    [Theory]
    [InlineData("done", FleetGlyphs.Done, FleetTones.Good)]
    [InlineData("idle", FleetGlyphs.Idle, FleetTones.Warn)]
    [InlineData("failed", FleetGlyphs.Failed, FleetTones.Bad)]
    [InlineData("working", FleetGlyphs.Working, FleetTones.Warn)]
    [InlineData("waiting", FleetGlyphs.Waiting, FleetTones.Bad)]
    [InlineData("stalled", FleetGlyphs.Stalled, FleetTones.Bad)]
    [InlineData(" Done ", FleetGlyphs.Done, FleetTones.Good)]
    public void Each_status_keeps_its_glyph_and_is_coloured_by_status(string status, string glyph, string tone)
    {
        var icon = StatusIcon.For(status)!;

        Assert.Equal(glyph, icon.Text);
        Assert.Equal(tone, icon.Tone);
    }

    [Fact]
    public void An_unknown_status_has_no_icon()
    {
        Assert.Null(StatusIcon.For("something else"));
    }
}
