using System.Drawing;
using Fleet.Ui;
using Terminal.Gui.App;
using Terminal.Gui.Drivers;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Fleet.Tests.Ui;

public sealed class CursorHeartbeatTests
{
    private static readonly Cursor Bar = new() { Position = new Point(4, 2), Style = CursorStyle.SteadyBar };

    [Fact]
    public void An_idle_hidden_cursor_is_settled() =>
        Assert.True(CursorHeartbeat.Settled(new Cursor(), focused: null));

    [Fact]
    public void A_focused_view_without_a_visible_cursor_counts_as_hidden() =>
        Assert.True(CursorHeartbeat.Settled(new Cursor(), new TextField()));

    [Fact]
    public void Focusing_a_text_input_needs_one_write_then_is_settled()
    {
        var field = new View { Cursor = Bar };

        Assert.False(CursorHeartbeat.Settled(new Cursor(), field));
        Assert.True(CursorHeartbeat.Settled(field.Cursor, field));
    }

    [Fact]
    public void A_redraw_that_parks_a_visible_cursor_is_not_settled() =>
        Assert.False(CursorHeartbeat.Settled(Bar with { Position = null }, new View { Cursor = Bar }));

    [Fact]
    public void A_moved_cursor_is_not_settled() =>
        Assert.False(CursorHeartbeat.Settled(Bar, new View { Cursor = Bar with { Position = new Point(5, 2) } }));

    [Fact]
    public void A_restyled_cursor_is_not_settled() =>
        Assert.False(CursorHeartbeat.Settled(Bar, new View { Cursor = Bar with { Style = CursorStyle.BlinkingBlock } }));

    [Fact]
    public void Leaving_a_text_input_is_not_settled_until_the_cursor_is_hidden() =>
        Assert.False(CursorHeartbeat.Settled(Bar, focused: null));

    [Fact]
    public void Quiet_ignores_an_app_without_a_driver()
    {
        var app = Application.Create();

        CursorHeartbeat.Quiet(app);

        app.RaiseIteration();
    }
}
