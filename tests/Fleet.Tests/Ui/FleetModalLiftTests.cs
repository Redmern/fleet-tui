using System.Drawing;
using Fleet.Shared.Constants;
using Fleet.Ui;
using Fleet.Ui.Models;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Views;

namespace Fleet.Tests.Ui;

// A dashboard tiled beside Claude asks the multiplexer to lift its pane over
// the whole window while a dialog is open. The dialog then fills the lifted
// pane (the multiplexer draws its border and title) and the pane goes back to
// its tile size when the dialog closes. FLEET_FRAMED is process-wide, so this
// runs in the serial ModalDepth collection.
[Collection(ModalDepthCollection.Name)]
public sealed class FleetModalLiftTests
{
    private const int TileCols = 50;

    private const int TileLines = 30;

    private static readonly IReadOnlyList<PickerEntry> Branches =
    [
        new("main"),
        new("feature/ABC-2512-some-long-description-with-many-words-in-it"),
    ];

    private static (Rectangle Frame, LineStyle Border, Size Lifted, IReadOnlyList<string> Rows, Size After, List<string> Calls) Open(
        Func<FloatScreen, (int Cols, int Rows)?> lift)
    {
        var calls = new List<string>();
        var framed = Environment.GetEnvironmentVariable(FramedPane.Variable);
        var (oldLift, oldDrop) = (FloatScreens.Lift, FloatScreens.Drop);
        Environment.SetEnvironmentVariable(FramedPane.Variable, "1");
        FloatScreens.Reset();
        FloatScreens.Lift = screen =>
        {
            calls.Add($"lift {screen.Title} {screen.Cols}x{screen.Rows}");
            return lift(screen);
        };
        FloatScreens.Drop = () =>
        {
            calls.Add("drop");
            return (TileCols, TileLines);
        };

        try
        {
            using var app = Application.Create().Init("ansi");
            app.Driver!.SetScreenSize(TileCols, TileLines);
            var frame = Rectangle.Empty;
            var border = LineStyle.None;
            var lifted = Size.Empty;
            IReadOnlyList<string> rows = [];

            app.Invoke(() =>
            {
                if (app.TopRunnableView is Window window)
                {
                    window.Layout();
                    frame = window.Frame;
                    border = window.BorderStyle ?? LineStyle.None;
                    lifted = app.Screen.Size;
                    rows = [.. window.SubViews.OfType<ListView>().Single().Source!.ToList().Cast<string>()];
                    app.RequestStop(app.TopRunnable);
                }
            });

            FleetPicker.Choose(app, "Base branch", Branches, Keymap.Default, new FleetPickerOptions(Filter: true), selected: 1);

            return (frame, border, lifted, rows, app.Screen.Size, calls);
        }
        finally
        {
            FloatScreens.Reset();
            (FloatScreens.Lift, FloatScreens.Drop) = (oldLift, oldDrop);
            Environment.SetEnvironmentVariable(FramedPane.Variable, framed);
        }
    }

    [Fact]
    public void A_lifted_picker_fills_the_lifted_pane_and_the_pane_drops_back_to_its_tile_on_close()
    {
        var (frame, border, lifted, rows, after, calls) = Open(s => (s.Cols, s.Rows));

        Assert.Equal(LineStyle.None, border);
        Assert.Equal(new Rectangle(Point.Empty, lifted), frame);
        Assert.True(lifted.Width > TileCols, $"lifted to {lifted}, wider than the {TileCols}-column tile");
        Assert.Equal(new Size(TileCols, TileLines), after);
        Assert.StartsWith("lift Base branch ", calls[0], StringComparison.Ordinal);
        Assert.Equal("drop", calls[^1]);
        Assert.All(rows, r => Assert.True(r.TrimEnd().Length <= frame.Width, $"'{r}' is wider than the lifted pane"));
    }

    [Fact]
    public void A_refused_lift_keeps_the_bordered_picker_clamped_inside_the_tile()
    {
        var (frame, border, _, _, after, calls) = Open(_ => null);

        Assert.NotEqual(LineStyle.None, border);
        Assert.True(frame.X >= 0 && frame.Right <= TileCols, $"frame {frame} leaves the {TileCols}-column tile");
        Assert.Equal(new Size(TileCols, TileLines), after);
        Assert.DoesNotContain("drop", calls);
    }
}
