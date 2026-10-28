using System.Drawing;
using Fleet.Ui;
using Fleet.Ui.Models;
using Terminal.Gui.App;
using Terminal.Gui.Views;

namespace Fleet.Tests.Ui;

// The dashboard runs in a pane narrower than the full window, so its picker
// must stay inside that pane's screen: a card wider than the screen is
// centred at a negative column and loses its border, title, filter and the
// start of every row.
[Collection(ModalDepthCollection.Name)]
public sealed class FleetPickerFitTests
{
    private const int Cols = 50;

    private const int Lines = 30;

    private static readonly IReadOnlyList<PickerEntry> Branches =
    [
        new("main"),
        new("develop"),
        new("feature/ABC-2512-some-long-description-with-many-words-in-it"),
        new("bugfix/ABC-2513-another-long-description-in-the-created-bill"),
    ];

    private static (Rectangle Frame, IReadOnlyList<string> Rows, string Footer) Open(FleetPickerOptions options)
    {
        using var app = Application.Create().Init("ansi");
        app.Driver!.SetScreenSize(Cols, Lines);
        var frame = Rectangle.Empty;
        IReadOnlyList<string> rows = [];
        var footer = string.Empty;

        app.Invoke(() =>
        {
            if (app.TopRunnableView is { } window)
            {
                window.Layout();
                frame = window.Frame;
                var list = window.SubViews.OfType<ListView>().Single();
                rows = [.. list.Source!.ToList().Cast<string>()];
                footer = window.SubViews.OfType<Label>()
                    .Select(l => l.Text.Replace("\n", string.Empty, StringComparison.Ordinal))
                    .FirstOrDefault(t => t.Contains("feature/", StringComparison.Ordinal)) ?? string.Empty;
                app.RequestStop(app.TopRunnable);
            }
        });

        FleetPicker.Choose(app, "Base branch", Branches, Keymap.Default, options, selected: 2);

        return (frame, rows, footer);
    }

    [Fact]
    public void A_picker_with_long_names_stays_inside_a_narrow_screen()
    {
        var (frame, _, _) = Open(new FleetPickerOptions(Filter: true));

        Assert.True(frame.X >= 0, $"left edge at {frame.X}");
        Assert.True(frame.Right <= Cols, $"right edge at {frame.Right}");
    }

    [Fact]
    public void Long_names_keep_their_start_and_every_row_fits()
    {
        var (frame, rows, _) = Open(new FleetPickerOptions(Filter: true));

        Assert.Contains(rows, r => r.Contains("feature/ABC-2512-", StringComparison.Ordinal) && r.Contains('…'));
        Assert.Contains(rows, r => r.Contains("main", StringComparison.Ordinal));
        Assert.All(rows, r => Assert.True(r.TrimEnd().Length <= frame.Width - 4, $"'{r}' is wider than the card"));
    }

    [Fact]
    public void The_full_name_of_a_cut_selected_row_shows_in_the_footer()
    {
        var (_, _, footer) = Open(new FleetPickerOptions(Filter: true));

        Assert.Contains("feature/ABC-2512-some-long-description-with-many-words-in-it", footer);
    }
}
