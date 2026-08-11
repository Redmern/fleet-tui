using Fleet.Ui;
using Fleet.Ui.Constants;
using Terminal.Gui.ViewBase;

namespace Fleet.Tests.Ui;

public class FleetThemeStatusTests
{
    [Fact]
    public void The_status_line_sits_in_the_bottom_right_in_its_own_muted_scheme()
    {
        var status = FleetTheme.StatusLine(Pos.AnchorEnd(2));

        Assert.Equal(Alignment.End, status.TextAlignment);
        Assert.Equal(FleetSchemes.Status, status.SchemeName);
        Assert.Equal(string.Empty, status.Text);
    }
}
