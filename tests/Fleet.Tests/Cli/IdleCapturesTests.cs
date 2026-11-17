using Fleet.Cli.Composition;
using Fleet.Features.Agents.ListAgents;
using Fleet.Platform.Mux.Fake;
using Fleet.Ports.Agents.Models;
using Fleet.Ports.Mux.Models;
using Fleet.Shared.Harness;
using Fleet.Shared.Settings.Models;
using Fleet.Tests.EndToEnd.MixedTeam;
using Fleet.Tests.Support;

namespace Fleet.Tests.Cli;

// AC-18 on recorded screens: the dashboard's pane watch on a fake clock. The pane shows captures of
// one idle screen in turn (only its clock, countdown or progress bar differs) for longer than the
// idle window, and the agent must read idle; new output inside the window must read working.
public sealed class IdleCapturesTests : IDisposable
{
    private static readonly TimeSpan Poll = TimeSpan.FromSeconds(10);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "fleet-tests", Path.GetRandomFileName());

    private readonly FakeMuxDriver _mux = new();

    private readonly SkippingClock _clock = new();

    private readonly List<(string Branch, TimeSpan Window)> _wentIdle = [];

    private SettingsConfig _settings = SettingsConfig.Default;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
        }
    }

    private PaneActivity Watch(Func<string, HarnessProfile?>? profiles = null) =>
        new(_mux, (agent, window) => _wentIdle.Add((agent.Branch, window)), _clock, settings: () => _settings, profiles: profiles);

    private async Task<(AgentRecord Agent, Pane Pane)> StartAsync(string harness)
    {
        var worktree = Path.Combine(_root, harness);
        Directory.CreateDirectory(worktree);
        var id = await _mux.SpawnAsync(new() { Cwd = worktree });
        var pane = (await _mux.ListPanesAsync()).Single(p => p.Id == id);

        return (new AgentRecord(worktree, "api", harness, harness, "origin/main", true, Open: true), pane);
    }

    private string Shown(PaneActivity watch, (AgentRecord Agent, Pane Pane) started, string capture)
    {
        _mux.SetText(started.Pane.Id, PaneCaptures.Read(started.Agent.Harness, capture));

        return watch.For([started.Agent], [started.Pane], _ => null).Single().Status;
    }

    private string ShownInTurn(
        PaneActivity watch, (AgentRecord Agent, Pane Pane) started, TimeSpan howLong, params string[] captures)
    {
        var status = string.Empty;

        for (var i = 0; i * Poll <= howLong; i++)
        {
            status = Shown(watch, started, captures[i % captures.Length]);
            _clock.Advance(Poll);
        }

        return status;
    }

    [Fact]
    public async Task A_pane_that_only_shows_its_volatile_parts_changing_goes_idle_after_the_window()
    {
        const string harness = "opencode";
        string[] captures = ["idle-120-a", "idle-120-b", "idle-120-c"];
        var started = await StartAsync(harness);
        var watch = Watch();

        Assert.Equal(AgentActivity.Working, ShownInTurn(watch, started, TimeSpan.FromSeconds(110), captures));
        Assert.Equal(AgentActivity.Idle, ShownInTurn(watch, started, TimeSpan.FromSeconds(20), captures));
        Assert.Equal([(harness, TimeSpan.FromSeconds(120))], _wentIdle);
    }

    [Theory]
    [InlineData("opencode", "fresh-120", "idle-120-a")]
    [InlineData("pi", "fresh-120", "idle-120-a")]
    public async Task New_output_inside_the_window_keeps_the_agent_working(string harness, string before, string after)
    {
        var started = await StartAsync(harness);
        var watch = Watch();

        ShownInTurn(watch, started, TimeSpan.FromSeconds(100), before);

        Assert.Equal(AgentActivity.Working, ShownInTurn(watch, started, TimeSpan.FromSeconds(100), after));
        Assert.Empty(_wentIdle);
        Assert.Equal(AgentActivity.Idle, ShownInTurn(watch, started, TimeSpan.FromSeconds(20), after));
    }

    [Fact]
    public async Task A_resize_that_only_reflows_in_the_middle_of_the_window_does_not_restart_it()
    {
        var started = await StartAsync("opencode");
        var watch = Watch();
        _mux.SetText(started.Pane.Id, "a long line of output that the harness wraps\nat the pane width");
        watch.For([started.Agent], [started.Pane], _ => null);
        _clock.Advance(TimeSpan.FromSeconds(60));
        _mux.SetText(started.Pane.Id, "a long line of output that the\nharness wraps at the pane width");
        watch.For([started.Agent], [started.Pane], _ => null);
        _clock.Advance(TimeSpan.FromSeconds(60));

        Assert.Equal(AgentActivity.Idle, watch.For([started.Agent], [started.Pane], _ => null).Single().Status);
    }

    // idle-90 clips the status line of idle-120, so the text differs: the window restarts once, and idle comes late.
    [Fact]
    public async Task A_resize_that_shows_other_text_restarts_the_window_once()
    {
        var started = await StartAsync("opencode");
        var watch = Watch();

        ShownInTurn(watch, started, TimeSpan.FromSeconds(100), "idle-120-a", "idle-120-c");

        Assert.Equal(AgentActivity.Working, ShownInTurn(watch, started, TimeSpan.FromSeconds(100), "idle-90"));
        Assert.Equal(AgentActivity.Idle, ShownInTurn(watch, started, TimeSpan.FromSeconds(20), "idle-90"));
        Assert.Single(_wentIdle);
    }

    // No shipped profile has its own window, so a changed opencode profile stands in for one.
    [Fact]
    public async Task The_window_of_the_running_harness_profile_replaces_the_default()
    {
        var slow = HarnessRegistry.Opencode with { IdleWindow = TimeSpan.FromSeconds(300) };
        var started = await StartAsync("opencode");
        var watch = Watch(name => name == slow.Name ? slow : HarnessRegistry.Find(name));

        Assert.Equal(AgentActivity.Working, ShownInTurn(watch, started, TimeSpan.FromSeconds(280), "idle-120-a", "idle-120-c"));
        Assert.Equal(AgentActivity.Idle, ShownInTurn(watch, started, TimeSpan.FromSeconds(20), "idle-120-a", "idle-120-c"));
        Assert.Equal([("opencode", TimeSpan.FromSeconds(300))], _wentIdle);
    }

    [Fact]
    public async Task The_users_setting_wins_over_the_window_of_the_profile()
    {
        _settings = SettingsConfig.Default with { IdleWindowSeconds = 45 };
        var slow = HarnessRegistry.Opencode with { IdleWindow = TimeSpan.FromSeconds(300) };
        var started = await StartAsync("opencode");
        var watch = Watch(_ => slow);

        ShownInTurn(watch, started, TimeSpan.FromSeconds(50), "idle-120-a", "idle-120-c");

        Assert.Equal([("opencode", TimeSpan.FromSeconds(45))], _wentIdle);
    }

    [Fact]
    public async Task The_window_comes_from_the_setting_and_is_handed_to_the_idle_notice()
    {
        _settings = SettingsConfig.Default with { IdleWindowSeconds = 45 };
        var started = await StartAsync("opencode");
        var watch = Watch();

        Assert.Equal(AgentActivity.Working, ShownInTurn(watch, started, TimeSpan.FromSeconds(30), "idle-120-a", "idle-120-b"));
        Assert.Equal(AgentActivity.Idle, ShownInTurn(watch, started, TimeSpan.FromSeconds(10), "idle-120-a", "idle-120-b"));
        Assert.Equal([("opencode", TimeSpan.FromSeconds(45))], _wentIdle);
    }

    [Fact]
    public async Task A_longer_window_from_the_setting_keeps_the_agent_working_past_the_default()
    {
        _settings = SettingsConfig.Default with { IdleWindowSeconds = 300 };
        var started = await StartAsync("opencode");
        var watch = Watch();

        Assert.Equal(AgentActivity.Working, ShownInTurn(watch, started, TimeSpan.FromSeconds(200), "idle-120-a", "idle-120-c"));
        Assert.Empty(_wentIdle);
    }
}
