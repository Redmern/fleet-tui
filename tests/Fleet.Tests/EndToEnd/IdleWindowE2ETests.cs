using Fleet.Platform.Storage;
using Fleet.Shared.Settings.Enums;
using Fleet.Tests.EndToEnd.MixedTeam;
using Fleet.Tests.Support;

namespace Fleet.Tests.EndToEnd;

// AC-16 end to end: one idle window setting for the machine, read by the dashboard's pane watch,
// list_agents and wait_for_agent through the real MCP composition root. 45 s is under the 120 s
// default, so each of the three is idle only if it read the setting.
public sealed class IdleWindowE2ETests : ConfigHomeFixture
{
    private const string Main = "";

    private readonly MixedTeamRig _team;

    public IdleWindowE2ETests() => _team = new MixedTeamRig(Path.Combine(ConfigHome, "pro\\ject"));

    private static (string, string)[] Agent(string repository, string branch) =>
        [("repository", repository), ("branch", branch)];

    private async Task<FakeHarness> StartOpencodeAsync()
    {
        await _team.AddRepositoryAsync("api");
        var started = await _team.CallAsync(
            Main, HarnessTool.NewAgent,
            ("repository", "api"), ("branch", "feat"), ("harness", "opencode"), ("task", "build the form"));

        Assert.False(started.IsError, started.Text);

        var worker = _team.Mux.HarnessIn(_team.Record("api", "feat").Worktree);
        await worker.Started;
        worker.Says("reading the form code");

        return worker;
    }

    [Fact]
    public async Task The_idle_window_setting_reaches_the_dashboard_list_agents_and_wait_for_agent()
    {
        FleetPaths.EnsureDirs();
        File.WriteAllText(FleetPaths.IdleSettingsFile, """{"version":1,"idleWindowSeconds":45}""");
        await StartOpencodeAsync();
        var dashboard = _team.Dashboard();

        Assert.Equal("working", (await _team.RefreshAsync(dashboard)).Single(a => a.Branch == "feat").Status);

        _team.Clock.Advance(TimeSpan.FromSeconds(46));

        Assert.Equal("idle", (await _team.RefreshAsync(dashboard)).Single(a => a.Branch == "feat").Status);
        Assert.Contains("api/feat — opencode, hidden [idle]", (await _team.CallAsync(Main, HarnessTool.ListAgents)).Text);
        Assert.StartsWith(
            "api/feat: idle — no pane output for 45s and no done report.",
            (await _team.CallAsync(Main, HarnessTool.WaitForAgent, Agent("api", "feat"))).Text);
    }

    [Fact]
    public async Task Without_the_setting_the_same_wait_is_not_idle_yet()
    {
        await StartOpencodeAsync();
        var dashboard = _team.Dashboard();
        await _team.RefreshAsync(dashboard);

        _team.Clock.Advance(TimeSpan.FromSeconds(46));

        Assert.Equal("working", (await _team.RefreshAsync(dashboard)).Single(a => a.Branch == "feat").Status);
        Assert.Contains("api/feat — opencode, hidden [working]", (await _team.CallAsync(Main, HarnessTool.ListAgents)).Text);
    }

    // The screen is a recorded opencode retry screen whose progress bar and countdown move. The first
    // list_agents writes the mark, the second sees another frame of the bar after the window.
    [Fact]
    public async Task List_agents_and_wait_for_agent_ignore_the_running_harnesss_volatile_parts()
    {
        var worker = await StartOpencodeAsync();
        _team.Mux.Inner.SetText(worker.Pane, PaneCaptures.Read("opencode", "idle-120-a"));

        Assert.Contains("api/feat — opencode, hidden [working]", (await _team.CallAsync(Main, HarnessTool.ListAgents)).Text);

        _team.Clock.Advance(TimeSpan.FromSeconds(121));
        _team.Mux.Inner.SetText(worker.Pane, PaneCaptures.Read("opencode", "idle-120-c"));

        Assert.Contains("api/feat — opencode, hidden [idle]", (await _team.CallAsync(Main, HarnessTool.ListAgents)).Text);

        _team.Mux.Inner.SetText(worker.Pane, PaneCaptures.Read("opencode", "idle-120-b"));

        Assert.StartsWith(
            "api/feat: idle — no pane output for 120s and no done report.",
            (await _team.CallAsync(Main, HarnessTool.WaitForAgent, Agent("api", "feat"))).Text);
    }
}
