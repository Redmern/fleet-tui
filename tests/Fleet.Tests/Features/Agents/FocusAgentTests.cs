using Fleet.Features.Agents.FocusAgent;
using Fleet.Platform.Mux.Fake;
using Fleet.Ports.Mux.Models;

namespace Fleet.Tests.Features.Agents;

public class FocusAgentTests
{
    [Fact]
    public async Task An_agent_is_found_by_its_worktree_not_by_a_pane_id()
    {
        var mux = new FakeMuxDriver();
        await mux.SpawnAsync(new SpawnOptions { Cwd = "C:/repos/techweb/backend/main" });
        var wanted = await mux.SpawnAsync(
            new SpawnOptions { Cwd = "C:/repos/techweb/backend/feature_login" });

        var result = await new FocusAgentHandler(mux)
            .HandleAsync("C:/repos/techweb/backend/feature_login");

        Assert.True(result.Succeeded, result.Error);

        var panes = await mux.ListPanesAsync();
        Assert.True(panes.Single(p => p.Id == wanted).IsActive);
    }

    [Fact]
    public async Task A_trailing_separator_still_matches_the_pane()
    {
        var mux = new FakeMuxDriver();
        await mux.SpawnAsync(new SpawnOptions { Cwd = "C:/repos/techweb/backend/main" });

        var result = await new FocusAgentHandler(mux)
            .HandleAsync("C:/repos/techweb/backend/main/");

        Assert.True(result.Succeeded, result.Error);
    }

    [Fact]
    public async Task An_agent_whose_pane_is_gone_says_so_rather_than_focusing_something_else()
    {
        var mux = new FakeMuxDriver();
        await mux.SpawnAsync(new SpawnOptions { Cwd = "C:/repos/techweb/backend/main" });

        var result = await new FocusAgentHandler(mux)
            .HandleAsync("C:/repos/techweb/backend/feature_login");

        Assert.False(result.Succeeded);
        Assert.Contains("no pane", result.Error);
    }
}
