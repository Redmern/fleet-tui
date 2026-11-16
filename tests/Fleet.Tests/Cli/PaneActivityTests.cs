using System.Collections.Concurrent;
using Fleet.Cli.Composition;
using Fleet.Features.Agents.ListAgents;
using Fleet.Features.Orchestrations.ListSubs;
using Fleet.Ports.Agents.Models;
using Fleet.Ports.Mux;
using Fleet.Ports.Mux.Enums;
using Fleet.Ports.Mux.Models;
using Fleet.Shared.Constants;
using Fleet.Shared.Status.Enums;
using Fleet.Shared.Status.Models;

namespace Fleet.Tests.Cli;

public class PaneActivityTests
{
    private static AgentRecord Agent(string branch, string owner = "", string harness = AgentHarness.Claude, string status = "") =>
        new($@"C:\wt\{branch}", "api", branch, harness, "origin/main", true, Owner: owner, Status: status);

    private static Pane PaneOf(AgentRecord agent) =>
        new(new PaneId($"pane-{agent.Branch}"), "w", "t", "s", agent.Branch, agent.Worktree, false);

    private static AgentReport? NoHook(AgentRecord _) => null;

    [Fact]
    public void Each_agent_pane_is_read_once_and_its_text_is_kept_for_the_notices()
    {
        var agents = new[] { Agent("a"), Agent("b") };
        var mux = new TextMux { Text = "thinking... esc to interrupt" };
        var activity = new PaneActivity(mux);

        var live = activity.For(agents, [.. agents.Select(PaneOf)], NoHook);

        Assert.All(live, a => Assert.Equal(AgentActivity.Working, a.Status));
        Assert.All(agents, a => Assert.Equal(1, mux.Reads(PaneOf(a).Id)));
        Assert.Equal("thinking... esc to interrupt", activity.Texts[agents[1].Worktree]);
    }

    [Fact]
    public void Panes_are_read_side_by_side_but_no_more_than_six_at_once()
    {
        var agents = Enumerable.Range(0, 12).Select(i => Agent($"wt{i}")).ToList();
        var mux = new TextMux { Delay = TimeSpan.FromMilliseconds(100) };

        var live = new PaneActivity(mux).For(agents, [.. agents.Select(PaneOf)], NoHook);

        // Side by side is shown by overlap, not by wall-clock time, which a busy CI runner stretches.
        Assert.Equal(agents.Select(a => a.Branch), live.Select(a => a.Branch));
        Assert.InRange(mux.MostAtOnce, 2, 6);
    }

    [Fact]
    public void An_opencode_agent_whose_pane_has_been_quiet_shows_idle_and_a_done_one_keeps_done()
    {
        var root = Path.Combine(Path.GetTempPath(), "fleet-tests", Path.GetRandomFileName());

        try
        {
            var quiet = Agent("q", harness: AgentHarness.Opencode) with { Worktree = Path.Combine(root, "q") };
            var done = Agent("d", harness: AgentHarness.Opencode, status: "done") with { Worktree = Path.Combine(root, "d") };
            var busy = Agent("b", harness: AgentHarness.Opencode) with { Worktree = Path.Combine(root, "b") };
            var mux = new TextMux { Text = "opencode > " };
            var past = DateTime.UtcNow.AddHours(-1);
            Fleet.Shared.Status.PaneQuiet.Observe(quiet.Worktree, mux.Text, past);
            Fleet.Shared.Status.PaneQuiet.Observe(done.Worktree, mux.Text, past);

            var live = new PaneActivity(mux).For([quiet, done, busy], [PaneOf(quiet), PaneOf(done), PaneOf(busy)], NoHook);

            Assert.Equal([AgentActivity.Idle, "done", AgentActivity.Working], live.Select(a => a.Status));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void An_opencode_agent_going_quiet_is_reported_once_and_a_done_one_is_not()
    {
        var root = Path.Combine(Path.GetTempPath(), "fleet-tests", Path.GetRandomFileName());

        try
        {
            var agent = Agent("q", harness: AgentHarness.Opencode) with { Worktree = Path.Combine(root, "q") };
            var done = Agent("d", harness: AgentHarness.Opencode, status: "done") with { Worktree = Path.Combine(root, "d") };
            var mux = new TextMux { Text = "opencode > " };
            var idle = new List<string>();
            var activity = new PaneActivity(mux, (a, _) => idle.Add(a.Branch));
            var panes = new[] { PaneOf(agent), PaneOf(done) };

            activity.For([agent, done], panes, NoHook);
            Backdate(agent.Worktree, mux.Text);
            Backdate(done.Worktree, mux.Text);
            activity.For([agent, done], panes, NoHook);
            activity.For([agent, done], panes, NoHook);

            Assert.Equal(["q"], idle);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void Backdate(string worktree, string text)
    {
        File.Delete(Path.Combine(worktree, ".fleet", Fleet.Shared.Status.PaneQuiet.FileName));
        Fleet.Shared.Status.PaneQuiet.Observe(worktree, text, DateTime.UtcNow.AddHours(-1));
    }

    [Fact]
    public void A_hooked_agent_that_needs_no_pane_text_is_not_read()
    {
        var agent = Agent("a");
        var mux = new TextMux();

        var live = new PaneActivity(mux).For(
            [agent],
            [PaneOf(agent)],
            a => new AgentReport(a.Worktree, "s", AgentState.Working, DateTime.UtcNow));

        Assert.Equal(AgentActivity.Working, Assert.Single(live).Status);
        Assert.Equal(0, mux.Reads(PaneOf(agent).Id));
    }

    [Fact]
    public void A_finished_sub_and_an_agent_without_a_pane_are_not_read()
    {
        var done = Agent("sub", harness: AgentHarness.Orchestrator, status: "done");
        var paneless = Agent("gone");
        var mux = new TextMux();

        var live = new PaneActivity(mux).For([done, paneless], [PaneOf(done)], NoHook);

        Assert.Equal([done, paneless], live);
        Assert.Equal(0, mux.TotalReads);
    }

    [Theory]
    [InlineData("done", "idle", "done")]
    [InlineData("failed", "idle", "failed")]
    [InlineData("done", "", "done")]
    [InlineData("done", "thinking... esc to interrupt", AgentActivity.Working)]
    [InlineData("", "idle", AgentActivity.Idle)]
    public void A_reported_done_or_failed_stays_while_the_session_is_idle_but_live_work_wins(
        string stored, string pane, string expected)
    {
        var agent = Agent("a", status: stored);

        var live = new PaneActivity(new TextMux { Text = pane }).For([agent], [PaneOf(agent)], NoHook);

        Assert.Equal(expected, Assert.Single(live).Status);
    }

    [Theory]
    [InlineData(AgentState.Idle, "done")]
    [InlineData(AgentState.Working, AgentActivity.Working)]
    [InlineData(AgentState.Blocked, AgentActivity.Waiting)]
    public void A_hooked_agent_that_reported_done_keeps_it_only_while_idle(AgentState state, string expected)
    {
        var agent = Agent("a", status: "done");

        var live = new PaneActivity(new TextMux { Text = "do you want to proceed?" }).For(
            [agent],
            [PaneOf(agent)],
            a => new AgentReport(a.Worktree, "s", state, DateTime.UtcNow));

        Assert.Equal(expected, Assert.Single(live).Status);
    }

    [Fact]
    public void The_agents_tab_and_the_subs_tab_never_share_an_agent_so_no_pane_is_read_for_both()
    {
        var listing = SubTree.Of(
        [
            Agent("solo"),
            Agent("sub", harness: AgentHarness.Orchestrator),
            Agent("child", owner: "sub"),
            Agent("orphan", owner: "missing"),
        ]);

        Assert.Empty(listing.Board.Intersect(listing.Flat.Select(e => e.Agent)));
        Assert.Equal(4, listing.Board.Count + listing.Flat.Count);
    }

    private static AgentRecord Running(string branch = "gone", string status = "working", bool open = true, bool hidden = false) =>
        Agent(branch, status: status) with { Open = open, Hidden = hidden };

    private static IReadOnlyList<Pane> Elsewhere() => [PaneOf(Agent("elsewhere"))];

    private static (PaneActivity Activity, List<AgentRecord> Lost) Watching()
    {
        var lost = new List<AgentRecord>();

        return (new PaneActivity(new TextMux(), lost: lost.Add), lost);
    }

    [Fact]
    public void A_working_agent_whose_seen_pane_is_missing_on_two_refreshes_in_a_row_is_lost_once()
    {
        var (activity, lost) = Watching();
        var agent = Running();

        activity.For([agent], [PaneOf(agent)], NoHook);
        activity.For([agent], Elsewhere(), NoHook);

        Assert.Empty(lost);

        activity.For([agent], Elsewhere(), NoHook);

        Assert.Equal(agent.Branch, Assert.Single(lost).Branch);

        activity.For([agent], Elsewhere(), NoHook);
        activity.For([agent], Elsewhere(), NoHook);

        Assert.Single(lost);
    }

    [Fact]
    public void An_empty_pane_listing_is_a_failed_listing_not_a_missing_pane()
    {
        var (activity, lost) = Watching();
        var agent = Running();

        activity.For([agent], [PaneOf(agent)], NoHook);

        for (var i = 0; i < 4; i++)
        {
            activity.For([agent], new List<Pane>(), NoHook);
        }

        Assert.Empty(lost);
    }

    [Fact]
    public void Two_refreshes_that_reuse_one_pane_listing_count_as_one()
    {
        var (activity, lost) = Watching();
        var agent = Running();
        var cached = Elsewhere();

        activity.For([agent], [PaneOf(agent)], NoHook);
        activity.For([agent], cached, NoHook);
        activity.For([agent], cached, NoHook);

        Assert.Empty(lost);

        activity.For([agent], Elsewhere(), NoHook);

        Assert.Single(lost);
    }

    [Fact]
    public void Two_agent_sets_checked_against_one_pane_listing_each_count_that_listing_once()
    {
        var (activity, lost) = Watching();
        var board = Running("board");
        var owned = Running("owned");
        IReadOnlyList<Pane> both = [PaneOf(board), PaneOf(owned)];

        activity.For([board], both, NoHook);
        activity.For([owned], both, NoHook);

        var first = Elsewhere();
        activity.For([board], first, NoHook);
        activity.For([owned], first, NoHook);

        Assert.Empty(lost);

        var second = Elsewhere();
        activity.For([board], second, NoHook);
        activity.For([owned], second, NoHook);

        Assert.Equal(["board", "owned"], lost.Select(a => a.Branch).Order());
    }

    [Fact]
    public void A_listing_that_the_caller_says_is_incomplete_is_not_counted()
    {
        var lost = new List<AgentRecord>();
        var activity = new PaneActivity(new TextMux(), lost: lost.Add, listingIsWhole: panes => panes.Count > 1);
        var agent = Running();

        activity.For([agent], [PaneOf(agent), PaneOf(Agent("x"))], NoHook);

        for (var i = 0; i < 4; i++)
        {
            activity.For([agent], Elsewhere(), NoHook);
        }

        Assert.Empty(lost);
    }

    [Fact]
    public void A_pane_that_comes_back_clears_the_count()
    {
        var (activity, lost) = Watching();
        var agent = Running();

        activity.For([agent], [PaneOf(agent)], NoHook);
        activity.For([agent], Elsewhere(), NoHook);
        activity.For([agent], [PaneOf(agent)], NoHook);
        activity.For([agent], Elsewhere(), NoHook);

        Assert.Empty(lost);
    }

    [Fact]
    public void An_agent_whose_pane_was_never_seen_is_not_lost()
    {
        var (activity, lost) = Watching();
        var agent = Running();

        for (var i = 0; i < 4; i++)
        {
            activity.For([agent], Elsewhere(), NoHook);
        }

        Assert.Empty(lost);
    }

    [Theory]
    [InlineData("done", true)]
    [InlineData("failed", true)]
    [InlineData("working", false)]
    public void Only_an_open_working_agent_can_be_lost(string status, bool open)
    {
        var (activity, lost) = Watching();
        var agent = Running(status: status, open: open);

        activity.For([agent], [PaneOf(agent)], NoHook);
        activity.For([agent], Elsewhere(), NoHook);
        activity.For([agent], Elsewhere(), NoHook);

        Assert.Empty(lost);
    }

    [Fact]
    public void A_hidden_agent_is_lost_like_any_other_because_hiding_keeps_the_pane_listed()
    {
        var (activity, lost) = Watching();
        var agent = Running(hidden: true);

        activity.For([agent], [PaneOf(agent)], NoHook);
        activity.For([agent], Elsewhere(), NoHook);
        activity.For([agent], Elsewhere(), NoHook);

        Assert.Single(lost);
    }

    [Fact]
    public void An_agent_that_never_reported_counts_as_working()
    {
        var (activity, lost) = Watching();
        var agent = Running(status: string.Empty);

        activity.For([agent], [PaneOf(agent)], NoHook);
        activity.For([agent], Elsewhere(), NoHook);
        activity.For([agent], Elsewhere(), NoHook);

        Assert.Single(lost);
    }

    private sealed class TextMux : IMuxDriver
    {
        private readonly ConcurrentDictionary<PaneId, int> _reads = new();
        private int _running;
        private int _most;

        public string Text { get; init; } = "idle";

        public TimeSpan Delay { get; init; }

        public int MostAtOnce => Volatile.Read(ref _most);

        public int TotalReads => _reads.Values.Sum();

        public int Reads(PaneId id) => _reads.GetValueOrDefault(id);

        public string Name => "fake";

        public MuxCaps Caps => default;

        public PaneId CurrentPane => PaneId.None;

        public async Task<string> GetTextAsync(PaneId id, CancellationToken ct = default)
        {
            var now = Interlocked.Increment(ref _running);
            int seen;
            while (now > (seen = Volatile.Read(ref _most)) && Interlocked.CompareExchange(ref _most, now, seen) != seen)
            {
            }

            try
            {
                _reads.AddOrUpdate(id, 1, (_, n) => n + 1);

                if (Delay > TimeSpan.Zero)
                {
                    await Task.Delay(Delay, ct);
                }

                return Text;
            }
            finally
            {
                Interlocked.Decrement(ref _running);
            }
        }

        public Task<bool> IsAvailableAsync(CancellationToken ct = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<Pane>> ListPanesAsync(CancellationToken ct = default) => throw new NotSupportedException();

        public Task<PaneId> SpawnAsync(SpawnOptions options, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<PaneId> SplitAsync(SplitOptions options, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<PaneId> SpawnFloatingAsync(PaneId over, SpawnOptions options, CancellationToken ct = default) => throw new NotSupportedException();

        public Task KillPaneAsync(PaneId id, CancellationToken ct = default) => throw new NotSupportedException();

        public Task MovePaneAsync(PaneId id, MovePaneOptions options, CancellationToken ct = default) => throw new NotSupportedException();

        public Task SetTitleAsync(PaneId id, string title, CancellationToken ct = default) => throw new NotSupportedException();

        public Task FocusPaneAsync(PaneId id, CancellationToken ct = default) => throw new NotSupportedException();

        public Task SendTextAsync(PaneId id, string text, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<Workspace>> ListWorkspacesAsync(CancellationToken ct = default) => throw new NotSupportedException();

        public Task ShowWorkspaceAsync(string name, CancellationToken ct = default) => throw new NotSupportedException();

        public Task CloseWorkspaceAsync(string name, CancellationToken ct = default) => throw new NotSupportedException();

        public Task OpenWindowAsync(string name, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
