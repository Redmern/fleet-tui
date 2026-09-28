using Fleet.Platform.Mux.Embedded.Model;
using Fleet.Shared.Constants;

namespace Fleet.Tests.Platform.Mux.Embedded;

public class MuxModelTests
{
    private readonly MuxModel _model = new();

    private (PaneState Claude, PaneState Dash) Project(string name)
    {
        var claude = _model.Spawn(name, $"C:/repos/{name}", ["claude"]);
        var dash = _model.Split(claude.Id, sideBySide: true, newFirst: false, 50, $"C:/repos/{name}", ["fleet", "dash"])!;
        return (claude, dash);
    }

    [Fact]
    public void Spawning_creates_the_workspace_with_one_tab_holding_the_pane()
    {
        var pane = _model.Spawn("techweb", "C:/repos/techweb", ["claude"]);

        var listed = _model.ListPanes().Single();
        Assert.Equal(pane.Id, listed.Id.Value);
        Assert.Equal("techweb", listed.WindowId);
        Assert.Equal("techweb", listed.SessionName);
        Assert.True(listed.IsActive);
    }

    [Fact]
    public void A_side_by_side_split_fills_the_width_with_one_divider_column()
    {
        var (claude, dash) = Project("techweb");
        var client = _model.Connect(100, 41, "techweb");

        var view = _model.View(client.Id)!;
        var left = view.Panes.Single(p => p.Pane == claude.Id).Area;
        var right = view.Panes.Single(p => p.Pane == dash.Id).Area;

        Assert.Equal(100, left.Width + 1 + right.Width);
        Assert.Equal(40, left.Height);
        Assert.Equal(left.Width, view.Dividers.Single().X);
        Assert.Equal(dash.Id, view.Focused);
    }

    [Fact]
    public void Showing_changes_only_the_client_that_asked()
    {
        Project("techweb");
        Project("fleet");
        var laptop = _model.Connect(100, 40, "techweb");
        var desktop = _model.Connect(100, 40, "techweb");

        Assert.True(_model.Show(laptop.Id, "fleet"));

        Assert.Equal("fleet", _model.Client(laptop.Id)!.Showing);
        Assert.Equal("techweb", _model.Client(desktop.Id)!.Showing);
        Assert.True(_model.ListWorkspaces(laptop.Id).Single(w => w.Name == "fleet").ShownHere);
        Assert.False(_model.ListWorkspaces(desktop.Id).Single(w => w.Name == "fleet").ShownHere);
    }

    [Fact]
    public void Showing_a_workspace_that_does_not_exist_changes_nothing()
    {
        Project("techweb");
        var client = _model.Connect(100, 40, "techweb");

        Assert.False(_model.Show(client.Id, "nope"));
        Assert.Equal("techweb", _model.Client(client.Id)!.Showing);
    }

    [Fact]
    public void Moving_a_pane_to_a_hidden_workspace_keeps_the_same_pane()
    {
        var (claude, _) = Project("techweb");
        var agent = _model.Spawn("techweb", "C:/repos/techweb/backend/x", ["claude"]);

        Assert.True(_model.Move(agent.Id, FleetWorkspaces.HiddenFor("techweb")));

        Assert.Same(agent, _model.Pane(agent.Id));
        Assert.Equal(
            FleetWorkspaces.HiddenFor("techweb"),
            _model.ListPanes().Single(p => p.Id.Value == agent.Id).SessionName);
        Assert.Equal(2, _model.PanesIn("techweb").Count);
        Assert.Contains(claude.Id, _model.PanesIn("techweb"));
    }

    [Fact]
    public void Killing_the_last_pane_removes_the_workspace_and_blanks_clients_showing_it()
    {
        var pane = _model.Spawn("techweb", "C:/repos/techweb", ["claude"]);
        var client = _model.Connect(100, 40, "techweb");

        _model.Kill(pane.Id);

        Assert.Empty(_model.ListWorkspaces(client.Id));
        Assert.Null(_model.Client(client.Id)!.Showing);
    }

    [Fact]
    public void Killing_one_side_of_a_split_gives_the_other_the_whole_tab()
    {
        var (claude, dash) = Project("techweb");
        var client = _model.Connect(100, 41, "techweb");

        _model.Kill(dash.Id);

        var view = _model.View(client.Id)!;
        Assert.Equal(new Rect(0, 1, 100, 40), view.Panes.Single().Area);
        Assert.Equal(claude.Id, view.Focused);
    }

    [Fact]
    public void Panes_take_the_size_of_the_most_recently_active_client_showing_them()
    {
        var (claude, _) = Project("techweb");
        var small = _model.Connect(80, 25, "techweb");
        _model.Connect(200, 51, "techweb");
        _model.Resizes();

        _model.Touch(small.Id);
        _model.Resizes();

        Assert.Equal(24, claude.Rows);
        Assert.True(claude.Cols < 80);
    }

    [Fact]
    public void A_hidden_workspace_keeps_its_panes_size_while_another_is_resized()
    {
        var (techweb, _) = Project("techweb");
        Project("fleet");
        var client = _model.Connect(120, 41, "techweb");
        _model.Resizes();
        var before = (techweb.Cols, techweb.Rows);

        _model.Show(client.Id, "fleet");
        _model.Resize(client.Id, 90, 30);
        var changed = _model.Resizes();

        Assert.Equal(before, (techweb.Cols, techweb.Rows));
        Assert.DoesNotContain(changed, c => c.Pane == techweb);
    }

    [Fact]
    public void Showing_a_workspace_at_the_size_it_already_has_resizes_nothing()
    {
        Project("techweb");
        Project("fleet");
        var client = _model.Connect(120, 41, "techweb");
        _model.Resizes();
        _model.Show(client.Id, "fleet");
        _model.Resizes();

        _model.Show(client.Id, "techweb");

        Assert.Empty(_model.Resizes());
    }

    [Fact]
    public void Joining_a_moved_pane_beside_another_puts_them_in_one_tab()
    {
        var main = _model.Spawn("techweb", "C:/x", ["claude"]);
        var browser = _model.Split(main.Id, true, false, 50, "C:/x", ["yazi"])!;

        _model.Move(main.Id, "techweb~hidden");
        _model.JoinBeside(main.Id, browser.Id, true, false, 50);

        var panes = _model.ListPanes();
        Assert.All(panes, p => Assert.Equal("techweb~hidden", p.SessionName));
        Assert.Single(panes.Select(p => p.TabId).Distinct());
    }

    [Fact]
    public void Focus_can_move_to_the_pane_on_the_left()
    {
        var (claude, dash) = Project("techweb");
        var client = _model.Connect(100, 41, "techweb");

        Assert.Equal(dash.Id, _model.View(client.Id)!.Focused);
        Assert.True(_model.FocusDirection(client.Id, -1, 0));
        Assert.Equal(claude.Id, _model.View(client.Id)!.Focused);
    }

    [Fact]
    public void Cycling_tabs_wraps_within_the_shown_workspace()
    {
        var first = _model.Spawn("techweb", "C:/x", ["claude"]);
        _model.Spawn("techweb", "C:/y", ["nvim"]);
        var client = _model.Connect(100, 40, "techweb");

        _model.CycleTab(client.Id, 1);

        Assert.Equal(first.Id, _model.View(client.Id)!.Focused);
    }

    [Fact]
    public void A_new_client_shows_the_first_workspace_that_is_not_hidden()
    {
        _model.Spawn(FleetWorkspaces.HiddenFor("techweb"), "C:/x", ["claude"]);
        _model.Spawn("techweb", "C:/y", ["claude"]);

        var client = _model.Connect(100, 40, null);

        Assert.Equal("techweb", client.Showing);
    }

    [Fact]
    public void Resizing_moves_the_divider_on_the_side_asked_for()
    {
        var (claude, dash) = Project("techweb");
        var client = _model.Connect(101, 41, "techweb");
        _model.Resizes();
        var before = claude.Cols;

        Assert.True(_model.ResizeFocused(client.Id, "left", 5));
        _model.Resizes();

        Assert.Equal(before - 5, claude.Cols);
        Assert.Equal(101, claude.Cols + 1 + dash.Cols);
        Assert.False(_model.ResizeFocused(client.Id, "up", 5));
    }

    [Fact]
    public void Zoom_gives_the_focused_pane_the_whole_tab_until_toggled_or_navigated()
    {
        var (claude, dash) = Project("techweb");
        var client = _model.Connect(100, 41, "techweb");

        Assert.True(_model.ToggleZoom(client.Id));
        var zoomed = _model.View(client.Id)!;
        Assert.Equal(new Rect(0, 1, 100, 40), zoomed.Panes.Single(p => p.Pane == dash.Id).Area);
        Assert.Empty(zoomed.Dividers);

        _model.FocusDirection(client.Id, -1, 0);

        var view = _model.View(client.Id)!;
        Assert.Equal(2, view.Panes.Count);
        Assert.Equal(claude.Id, view.Focused);
    }

    [Fact]
    public void A_lone_pane_does_not_zoom()
    {
        _model.Spawn("techweb", "C:/x", ["claude"]);
        var client = _model.Connect(100, 41, "techweb");

        _model.ToggleZoom(client.Id);

        Assert.Null(_model.View(client.Id)!.Tab!.Zoomed);
    }

    [Fact]
    public void Next_pane_cycles_through_the_tab_and_tabs_are_chosen_by_number()
    {
        var (claude, dash) = Project("techweb");
        var other = _model.Spawn("techweb", "C:/x", ["shell"]);
        var client = _model.Connect(100, 41, "techweb");

        Assert.True(_model.FocusTabIndex(client.Id, 0));
        Assert.Equal(dash.Id, _model.View(client.Id)!.Focused);
        _model.NextPane(client.Id);
        Assert.Equal(claude.Id, _model.View(client.Id)!.Focused);

        Assert.True(_model.FocusTabIndex(client.Id, 1));
        Assert.Equal(other.Id, _model.View(client.Id)!.Focused);
        Assert.False(_model.FocusTabIndex(client.Id, 5));
    }

    [Fact]
    public void Focus_can_move_from_a_named_pane_without_a_client_asking()
    {
        var (claude, dash) = Project("techweb");
        _model.Connect(100, 41, "techweb");

        Assert.True(_model.FocusDirectionFrom(dash.Id, -1, 0));

        Assert.Equal(claude.Id, _model.Workspace("techweb")!.Tabs[0].ActivePane);
        Assert.False(_model.FocusDirectionFrom(claude.Id, -1, 0));
    }

    [Fact]
    public void Nvim_is_recognised_by_program_wrapper_or_title()
    {
        var plain = _model.Spawn("a", "C:/x", ["C:/tools/nvim.exe"]);
        var titled = _model.Spawn("b", "C:/x", ["fleet", "titled", "--title", "files", "--", "nvim", "-c", "x"]);
        var shell = _model.Spawn("c", "C:/x", ["pwsh"]);
        var renamed = _model.Spawn("d", "C:/x", ["pwsh"]);
        renamed.Title = "README.md - NVIM";

        Assert.True(_model.IsNvim(plain.Id));
        Assert.True(_model.IsNvim(titled.Id));
        Assert.False(_model.IsNvim(shell.Id));
        Assert.True(_model.IsNvim(renamed.Id));
    }
}