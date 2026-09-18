using Fleet.Features.Projects.OpenProject;
using Fleet.Features.Projects.OpenProject.Models;
using Fleet.Platform.Mux;
using Fleet.Platform.Mux.Fake;
using Fleet.Ports.Projects.Models;
using Fleet.Shared.Constants;

namespace Fleet.Tests.Features.Projects;

public class OpenProjectTests
{
    private static readonly Project Backend = new("backend", "/repos/backend");

    private static OpenProjectCommand Command =>
        new(Backend, Harness: "claude", FleetExecutable: "fleet");

    [Fact]
    public async Task Opens_a_harness_pane_and_a_dashboard_pane_in_one_window()
    {
        var mux = new FakeMuxDriver();

        var result = await new OpenProjectHandler(mux).HandleAsync(Command);

        Assert.True(result.Succeeded);

        var panes = await mux.ListPanesAsync();
        Assert.Equal(2, panes.Count);
        Assert.Single(panes.Select(p => p.WindowId).Distinct());
    }

    [Fact]
    public async Task The_left_pane_runs_the_harness()
    {
        var mux = new FakeMuxDriver();

        var result = await new OpenProjectHandler(mux).HandleAsync(Command);

        Assert.Equal(["claude"], mux.ArgsFor(result.Value.HarnessPane));
    }

    [Fact]
    public async Task The_right_pane_runs_the_dashboard_for_this_project()
    {
        var mux = new FakeMuxDriver();

        var result = await new OpenProjectHandler(mux).HandleAsync(Command);

        Assert.Equal(
            ["fleet", "dash", "--project", "backend"],
            mux.ArgsFor(result.Value.DashPane));
    }

    [Fact]
    public async Task Both_panes_open_in_the_project_root()
    {
        var mux = new FakeMuxDriver();

        await new OpenProjectHandler(mux).HandleAsync(Command);

        Assert.All(await mux.ListPanesAsync(), p => Assert.Equal("/repos/backend", p.Cwd));
    }

    [Fact]
    public async Task The_window_is_titled_after_the_project()
    {
        var mux = new FakeMuxDriver();

        var result = await new OpenProjectHandler(mux).HandleAsync(Command);

        Assert.Equal(FleetTabTitles.Dashboard, mux.TitleOf(result.Value.HarnessPane));
    }

    [Fact]
    public async Task The_dashboard_pane_ends_up_focused()
    {
        var mux = new FakeMuxDriver();

        var result = await new OpenProjectHandler(mux).HandleAsync(Command);

        var panes = await mux.ListPanesAsync();
        Assert.True(panes.Single(p => p.Id == result.Value.DashPane).IsActive);
        Assert.False(panes.Single(p => p.Id == result.Value.HarnessPane).IsActive);
    }

    [Fact]
    public async Task The_session_is_named_after_the_project()
    {
        var mux = new FakeMuxDriver();

        await new OpenProjectHandler(mux).HandleAsync(Command);

        Assert.All(await mux.ListPanesAsync(), p => Assert.Equal("backend", p.SessionName));
    }

    [Fact]
    public async Task Reports_a_failure_when_the_mux_is_unavailable()
    {
        var mux = new FailSilentDriver(new FakeMuxDriver { Available = false }, _ => { });

        var result = await new OpenProjectHandler(mux).HandleAsync(Command);

        Assert.False(result.Succeeded);
        Assert.Contains("did not respond", result.Error);
        Assert.Contains("fleet doctor", result.Error);
    }
}
