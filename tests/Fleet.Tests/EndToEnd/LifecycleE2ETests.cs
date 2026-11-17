using Fleet.Cli.Composition;
using Fleet.Shared.Constants;
using Fleet.Shared.Mcp;
using Fleet.Shared.Settings.Enums;
using Fleet.Tests.EndToEnd.MixedTeam;

namespace Fleet.Tests.EndToEnd;

// AC-6 (pane path), AC-7, AC-14 and AC-15 through the real McpActions and the dashboard's PaneActivity.
public sealed class LifecycleE2ETests : ConfigHomeFixture
{
    private const string Main = "";

    private readonly MixedTeamRig _team;

    public LifecycleE2ETests() => _team = new MixedTeamRig(Path.Combine(ConfigHome, "pro\\ject"));

    private string NoticesFile => Path.Combine(_team.Root, ".fleet", AgentHarness.NoticesFile);

    private static (string, string)[] Agent(string repository, string branch) =>
        [("repository", repository), ("branch", branch)];

    [Fact]
    public async Task Stopping_an_agent_clears_the_address_fleet_would_hand_out_for_it()
    {
        var repository = await _team.AddRepositoryAsync("api");
        var fix = await _team.StartClaudeWorkerAsync(Main, "api", repository, "fix");
        await _team.ClaudeHookReportsInboxAsync(fix, "uds:fix");

        Assert.Equal(_team.InboxAddress("uds:fix"), await Adapters.AgentInboxes().AddressAsync(fix.Worktree));

        var stopped = await _team.CallAsync(Main, HarnessTool.StopAgent, Agent("api", "fix"));

        Assert.False(stopped.IsError, stopped.Text);
        Assert.Null(await Adapters.AgentInboxes().AddressAsync(fix.Worktree));
    }

    [Fact]
    public async Task A_socket_that_disappears_is_no_longer_handed_out()
    {
        var repository = await _team.AddRepositoryAsync("api");
        var fix = await _team.StartClaudeWorkerAsync(Main, "api", repository, "fix");
        await _team.ClaudeHookReportsInboxAsync(fix, "uds:fix");
        File.Delete(_team.InboxAddress("uds:fix")["uds:".Length..]);

        Assert.Null(await Adapters.AgentInboxes().AddressAsync(fix.Worktree));
    }

    [Fact]
    public async Task Removing_a_working_sub_needs_stop_and_stop_removes_it_in_one_call()
    {
        var sub = await _team.DispatchAsync(Main, "upgrade everything", AgentHarness.Opencode);

        var refused = await _team.CallAsync(Main, HarnessTool.RemoveSub, ("slug", sub.Slug));

        Assert.True(refused.IsError);
        Assert.Contains("still working", refused.Text);
        Assert.Contains(_team.Store.List(_team.Project), a => a.Branch == sub.Slug);

        var removed = await _team.CallAsync(Main, HarnessTool.RemoveSub, ("slug", sub.Slug), ("stop", "true"));

        Assert.False(removed.IsError, removed.Text);
        Assert.DoesNotContain(_team.Store.List(_team.Project), a => a.Branch == sub.Slug);
        Assert.Empty(await _team.Mux.ListPanesAsync());
    }

    [Fact]
    public async Task A_working_agent_whose_pane_vanishes_is_failed_and_the_owner_is_told()
    {
        var repository = await _team.AddRepositoryAsync("api");
        await _team.StartClaudeWorkerAsync(Main, "api", repository, "fix");
        var worker = _team.Record("api", "fix");
        await _team.Mux.SpawnAsync(new Fleet.Ports.Mux.Models.SpawnOptions { Cwd = _team.Root });
        var dashboard = _team.Dashboard();

        await _team.RefreshAsync(dashboard);
        await _team.Mux.KillPaneAsync(_team.Mux.HarnessIn(worker.Worktree).Pane);
        await _team.RefreshAsync(dashboard);

        Assert.NotEqual(OrchestrationStatus.Failed, _team.Record("api", "fix").Status);

        await _team.RefreshAsync(dashboard);

        await MixedTeamRig.UntilAsync(
            () => _team.Record("api", "fix").Status == OrchestrationStatus.Failed, "the lost pane was not failed");
        Assert.Equal("session ended without a report", _team.Record("api", "fix").Summary);
        await MixedTeamRig.UntilAsync(
            () => File.Exists(NoticesFile) && File.ReadAllText(NoticesFile).Contains("api/fix: failed", StringComparison.Ordinal),
            "the owner was not told");
    }
}
