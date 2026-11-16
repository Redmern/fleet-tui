using System.Reflection;
using Fleet.Cli.Composition;
using Fleet.Ports.Agents.Models;
using Fleet.Shared.Constants;
using Fleet.Tests.Support;

namespace Fleet.Tests.Cli;

// The dashboard's stall check and its auto-close of finished agents both ask how long a pane has
// stood still. StillPanes answers that with the shared pane normal form and the running harness's
// volatile patterns.
public sealed class StillPanesTests
{
    private static readonly DateTime T0 = new(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);

    private static AgentRecord Agent(string harness, string branch = "feat") =>
        new($@"C:\wt\{branch}", "api", branch, harness, "origin/main", true);

    [Fact]
    public void A_pane_stands_still_from_the_first_time_its_settled_text_was_seen()
    {
        var still = new StillPanes();
        var agent = Agent(AgentHarness.Claude);

        Assert.Equal(T0, still.Since(agent, "output", T0));
        Assert.Equal(T0, still.Since(agent, "output  \n", T0.AddMinutes(5)));
        Assert.Equal(T0.AddMinutes(6), still.Since(agent, "output\nmore", T0.AddMinutes(6)));
    }

    [Fact]
    public void The_running_harnesss_volatile_parts_do_not_move_it()
    {
        var still = new StillPanes();
        var agent = Agent(AgentHarness.Opencode);

        still.Since(agent, PaneCaptures.Read("opencode", "idle-120-a"), T0);

        Assert.Equal(T0, still.Since(agent, PaneCaptures.Read("opencode", "idle-120-b"), T0.AddMinutes(1)));
        Assert.Equal(T0, still.Since(agent, PaneCaptures.Read("opencode", "idle-120-c"), T0.AddMinutes(2)));
    }

    // The progress bar is opencode's own pattern: on a claude pane the same change is output.
    [Fact]
    public void Another_harnesss_pattern_does_not_apply()
    {
        var still = new StillPanes();
        var agent = Agent(AgentHarness.Claude);

        still.Since(agent, PaneCaptures.Read("opencode", "idle-120-b"), T0);

        Assert.Equal(T0.AddMinutes(1), still.Since(agent, PaneCaptures.Read("opencode", "idle-120-c"), T0.AddMinutes(1)));
    }

    [Fact]
    public void The_busy_lines_timer_and_token_count_do_not_move_it()
    {
        var still = new StillPanes();
        var agent = Agent(AgentHarness.Claude);

        still.Since(agent, "output line\n* Churning... (12s · esc to interrupt)\n  3 tokens", T0);

        Assert.Equal(
            T0,
            still.Since(agent, "output line\n* Churning... (84s · esc to interrupt)\n  97 tokens", T0.AddMinutes(1)));
    }

    // Before the shared normal form these watches dropped every digit, so a number that fleet does
    // not know as a counter never moved them. Now it is output: the stall and auto-close timers restart.
    [Theory]
    [InlineData("progress 45%", "progress 46%")]
    [InlineData("12 files changed", "13 files changed")]
    [InlineData("exit code 1", "exit code 2")]
    public void A_number_that_is_no_known_counter_moves_it(string before, string after)
    {
        var still = new StillPanes();
        var agent = Agent(AgentHarness.Claude);

        still.Since(agent, before, T0);

        Assert.Equal(T0.AddMinutes(1), still.Since(agent, after, T0.AddMinutes(1)));
    }

    [Fact]
    public void Each_worktree_has_its_own_time_and_a_forgotten_one_starts_again()
    {
        var still = new StillPanes();
        var one = Agent(AgentHarness.Claude, "one");
        var two = Agent(AgentHarness.Claude, "two");

        still.Since(one, "output", T0);
        still.Since(two, "output", T0.AddMinutes(1));
        still.Forget(one.Worktree);

        Assert.Equal(T0.AddMinutes(2), still.Since(one, "output", T0.AddMinutes(2)));
        Assert.Equal(T0.AddMinutes(1), still.Since(two, "output", T0.AddMinutes(2)));
    }

    [Fact]
    public void The_dashboards_stall_and_auto_close_watches_both_go_through_it()
    {
        var root = typeof(StillPanesTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .First(a => a.Key == "RepoRoot").Value!;
        var wiring = File.ReadAllText(Path.Combine(root, "src", "Fleet", "Cli", "Composition", "DashboardWiring.cs"));

        Assert.Contains("var quiet = new StillPanes();", wiring, StringComparison.Ordinal);
        Assert.Contains("var idleSeen = new StillPanes();", wiring, StringComparison.Ordinal);
        Assert.Contains("quiet.Since(agent, text, now)", wiring, StringComparison.Ordinal);
        Assert.Contains("idleSeen.Since(agent, text, now)", wiring, StringComparison.Ordinal);
        Assert.DoesNotContain("PaneSettled.", wiring, StringComparison.Ordinal);
    }
}
