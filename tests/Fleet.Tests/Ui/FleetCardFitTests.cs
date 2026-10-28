using System.Drawing;
using Fleet.Ui;
using Fleet.Ui.Constants;
using Terminal.Gui.App;
using Terminal.Gui.Views;

namespace Fleet.Tests.Ui;

// The New agent card runs in the dashboard pane too: a long base branch must
// stay readable inside a narrow card instead of running off its right edge.
[Collection(ModalDepthCollection.Name)]
public sealed class FleetCardFitTests
{
    private const string Long = "feature/ABC-2512-some-long-description-with-many-words-in-it";

    private static FleetCardField Base() => new(FleetIcons.Base, "Base", Long, () => { });

    [Fact]
    public void A_long_value_keeps_its_start_and_end_and_fits_the_width()
    {
        var row = FleetCard.FieldRow(Base(), "b", 40);
        var value = row.Spans[^1].Text;

        Assert.StartsWith("feature/ABC-", value);
        Assert.EndsWith("in-it", value);
        Assert.Contains('…', value);
        Assert.True(row.Text.Length <= 38, $"'{row.Text}' is wider than the row");
    }

    [Fact]
    public void A_short_value_is_left_alone()
    {
        var row = FleetCard.FieldRow(new FleetCardField(FleetIcons.Base, "Base", "main", () => { }), "b", 40);

        Assert.Equal("main", row.Spans[^1].Text);
    }

    [Fact]
    public void The_card_cuts_long_values_on_a_narrow_screen()
    {
        using var app = Application.Create().Init("ansi");
        app.Driver!.SetScreenSize(50, 30);
        var frame = Rectangle.Empty;
        IReadOnlyList<string> rows = [];

        app.Invoke(() =>
        {
            if (app.TopRunnableView is { } window)
            {
                window.Layout();
                frame = window.Frame;
                rows = [.. window.SubViews.OfType<ListView>().Single().Source!.ToList().Cast<string>()];
                app.RequestStop(app.TopRunnable);
            }
        });

        FleetCard.Show(app, "New agent", Keymap.Default, () => [Base()], (FleetIcons.Create, "Create", "c"));

        Assert.True(frame.X >= 0 && frame.Right <= 50, $"card at {frame}");
        Assert.Contains(rows, r => r.Contains("feature/ABC-", StringComparison.Ordinal) && r.Contains('…'));
        Assert.All(rows, r => Assert.True(r.TrimEnd().Length <= frame.Width - 4, $"'{r}' is wider than the card"));
    }
}
