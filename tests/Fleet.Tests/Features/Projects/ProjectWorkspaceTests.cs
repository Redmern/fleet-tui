using Fleet.Features.Projects;
using Fleet.Ports.Mux.Models;

namespace Fleet.Tests.Features.Projects;

public class ProjectWorkspaceTests
{
    private static Pane Dash(string sessionName) =>
        new(new PaneId("1"), "w1", "t1", sessionName, "Dashboard", "/repos/backend", true);

    [Fact]
    public void No_dash_pane_is_never_native()
    {
        Assert.False(ProjectWorkspace.IsNative(null, "backend"));
    }

    [Fact]
    public void A_dash_whose_session_matches_the_project_is_native()
    {
        Assert.True(ProjectWorkspace.IsNative(Dash("backend"), "backend"));
    }

    [Fact]
    public void A_dash_whose_session_matches_only_by_case_is_still_native()
    {
        Assert.True(ProjectWorkspace.IsNative(Dash("Backend"), "backend"));
    }

    [Fact]
    public void A_dash_in_some_other_workspace_is_not_native()
    {
        Assert.False(ProjectWorkspace.IsNative(Dash("default"), "backend"));
    }

    [Fact]
    public void A_dash_parked_in_the_hidden_workspace_is_not_native()
    {
        Assert.False(ProjectWorkspace.IsNative(Dash("hidden"), "backend"));
    }
}
