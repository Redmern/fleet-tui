using Fleet.Platform.Mux.Embedded.Model;
using Fleet.Shared.Constants;

namespace Fleet.Tests.Platform.Mux.Embedded;

public class HeadFloatTests
{
    private readonly MuxModel _model = new();

    private ClientState Attach(string workspace)
    {
        _model.Spawn(workspace, "C:/x", ["claude"]);
        return _model.Connect(100, 41, workspace);
    }

    [Fact]
    public void Without_a_head_the_toggle_says_it_is_missing()
    {
        var client = Attach("techweb");

        Assert.Equal(HeadToggle.Missing, _model.ToggleHead(client.Id));
    }

    [Fact]
    public void A_new_head_is_a_large_modal_float_that_takes_the_keys()
    {
        var client = Attach("techweb");

        var head = _model.SpawnHead("techweb", "C:/home", ["fleet", "head"]);

        var view = _model.View(client.Id)!;
        Assert.Equal(head.Id, view.Focused);
        Assert.Equal(new Rect(10, 5, 80, 32), view.FloatingPanes.Single().Area);
        Assert.Same(head, _model.HeadPane());
        Assert.False(_model.ToFloat(head.Id));
        Assert.False(_model.ToTile(head.Id));
    }

    [Fact]
    public void Toggling_hides_the_head_without_killing_it_and_shows_it_again()
    {
        var client = Attach("techweb");
        var tile = _model.PanesIn("techweb").Single();
        var head = _model.SpawnHead("techweb", "C:/home", ["fleet", "head"]);

        Assert.Equal(HeadToggle.Hidden, _model.ToggleHead(client.Id));

        var hidden = _model.View(client.Id)!;
        Assert.Empty(hidden.FloatingPanes);
        Assert.Equal(tile, hidden.Focused);
        Assert.Contains(head.Id, _model.PanesIn(MuxModel.HeadHolding));
        Assert.True(FleetWorkspaces.IsHidden(MuxModel.HeadHolding));
        Assert.DoesNotContain(MuxModel.HeadHolding, client.Projects);

        Assert.Equal(HeadToggle.Shown, _model.ToggleHead(client.Id));

        Assert.Equal(head.Id, _model.View(client.Id)!.Focused);
        Assert.Null(_model.Workspace(MuxModel.HeadHolding));
    }

    [Fact]
    public void The_head_follows_the_window_into_another_project()
    {
        var client = Attach("techweb");
        _model.Spawn("api", "C:/api", ["claude"]);
        var head = _model.SpawnHead("techweb", "C:/home", ["fleet", "head"]);

        _model.Show(client.Id, "api");

        Assert.Equal(HeadToggle.Shown, _model.ToggleHead(client.Id));
        Assert.Contains(head.Id, _model.PanesIn("api"));
        Assert.DoesNotContain(head.Id, _model.PanesIn("techweb"));
        Assert.Equal(head.Id, _model.View(client.Id)!.Focused);
    }

    [Fact]
    public void Hiding_ordinary_floats_leaves_the_head_alone()
    {
        var client = Attach("techweb");
        _model.SpawnFloat("techweb", "C:/x", ["pwsh"]);
        var head = _model.SpawnHead("techweb", "C:/home", ["fleet", "head"]);

        _model.ToggleFloats(client.Id);

        Assert.Equal(head.Id, _model.View(client.Id)!.FloatingPanes.Single().Pane);
    }
}
