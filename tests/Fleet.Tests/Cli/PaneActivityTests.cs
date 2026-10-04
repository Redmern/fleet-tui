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
