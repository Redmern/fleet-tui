using Fleet.Ui;
using Fleet.Ui.Constants;
using Fleet.Ui.Models;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Fleet.Tests.Ui;

// The dashboard re-binds its rows, tab titles and action bar every refresh (4 s). When nothing changed
// these must leave the views clean, or Terminal.Gui repaints the whole screen idle.
public sealed class UnchangedRedrawTests : IDisposable
{
    private readonly IApplication _app = Application.Create().Init("ansi");
    private readonly Window _window = new() { Width = 80, Height = 20 };

    public void Dispose()
    {
        _window.Dispose();
        _app.Dispose();
    }

    private static FleetRow[] Rows() =>
    [
        new([new FleetSpan("alpha", FleetTones.Normal), FleetSpan.Muted(" main")]),
        new([FleetSpan.Plain("bravo")], [FleetSpan.Muted("2m")]),
    ];

    private static IReadOnlyList<(string Key, string Label, Action Run)> Bar() =>
        [("n", FleetIcons.Back, () => { }), ("enter", FleetIcons.Select, () => { })];

    private void Settle()
    {
        _app.LayoutAndDraw(true);
        Assert.False(_window.NeedsDraw || _window.NeedsLayout);
    }

    private ListView Shown(ListView list, IReadOnlyList<FleetRow> rows)
    {
        _window.Add(list);
        _app.Begin(_window);
        FleetRows.Fill(list, rows);
        Settle();
        return list;
    }

    [Fact]
    public void Filling_the_same_rows_again_does_not_redraw_the_list()
    {
        var list = Shown(FleetTheme.Rows(1, 0, Dim.Fill()), Rows());

        FleetRows.Fill(list, Rows(), FleetRows.Selected(list));

        Assert.False(list.NeedsDraw);
    }

    [Fact]
    public void Filling_the_same_rows_keeps_the_selection_it_was_given()
    {
        var list = Shown(FleetTheme.Rows(1, 0, Dim.Fill()), Rows());

        FleetRows.Fill(list, Rows(), 1);

        Assert.Equal(1, FleetRows.Selected(list));
    }

    [Fact]
    public void Filling_changed_rows_redraws_the_list()
    {
        var list = Shown(FleetTheme.Rows(1, 0, Dim.Fill()), Rows());

        FleetRows.Fill(list, [.. Rows(), FleetRow.Plain("charlie")], FleetRows.Selected(list));

        Assert.True(list.NeedsDraw);
        Assert.Equal(3, list.Source!.Count);
    }

    [Fact]
    public void Retitling_a_tab_with_its_current_title_does_not_relayout()
    {
        var bar = new FleetTabBar(0, 0, ["Agents (2)", "Subs (0)"]);
        _window.Add(bar.Root);
        _app.Begin(_window);
        Settle();

        bar.Retitle(0, "Agents (2)");

        Assert.False(_window.NeedsLayout);
        Assert.False(bar.Root.NeedsDraw);
    }

    [Fact]
    public void Showing_the_same_actions_again_does_not_relayout()
    {
        var hints = new FleetActionBar(Pos.AnchorEnd(1));
        _window.Add(hints.Root);
        _app.Begin(_window);
        hints.Show(Bar());
        Settle();

        hints.Show(Bar());

        Assert.False(_window.NeedsLayout);
        Assert.False(hints.Root.NeedsDraw);
    }

    [Fact]
    public void Showing_the_same_actions_runs_the_newest_callbacks()
    {
        var hints = new FleetActionBar(Pos.AnchorEnd(1));
        var ran = string.Empty;
        hints.Show([("n", FleetIcons.Back, () => ran = "old")]);

        hints.Show([("n", FleetIcons.Back, () => ran = "new")]);
        hints.Items[0].Run();

        Assert.Equal("new", ran);
    }
}
