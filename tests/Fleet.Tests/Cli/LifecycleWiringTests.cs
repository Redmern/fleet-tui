using System.Reflection;

namespace Fleet.Tests.Cli;

public sealed class LifecycleWiringTests
{
    private static string Source(params string[] parts)
    {
        var root = typeof(LifecycleWiringTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .First(a => a.Key == "RepoRoot").Value!;

        return File.ReadAllText(Path.Combine([root, "src", "Fleet", .. parts]));
    }

    [Fact]
    public void The_dashboard_watches_panes_through_the_owner_notices_that_fail_a_lost_session()
    {
        var wiring = Source("Cli", "Composition", "DashboardWiring.cs");

        Assert.Contains("ownerNotices.Watch()", wiring, StringComparison.Ordinal);
        Assert.DoesNotContain("new PaneActivity(", wiring, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_dashboard_and_mcp_stop_and_remove_gets_the_hook_state_store()
    {
        var dashboard = Source("Cli", "Composition", "DashboardWiring.cs");
        var mcp = Source("Cli", "Composition", "McpActions.cs");

        Assert.Contains("new StopAgentHandler(mux, agents, Adapters.AgentStates()", dashboard, StringComparison.Ordinal);
        Assert.Contains("new RemoveAgentHandler(git, mux, agents, hookState: Adapters.AgentStates())", dashboard, StringComparison.Ordinal);
        Assert.Contains("StopAgentHandler _stopper = new(mux, store, Adapters.AgentStates()", mcp, StringComparison.Ordinal);
        Assert.Contains("RemoveAgentHandler _remover = new(git, mux, store, hookState: Adapters.AgentStates())", mcp, StringComparison.Ordinal);
        Assert.Contains("new RemoveAgentHandler(git, mux, store, hookState: Adapters.AgentStates())", mcp, StringComparison.Ordinal);
        Assert.Contains("new StopAgentHandler(mux, store, Adapters.AgentStates()", mcp, StringComparison.Ordinal);
    }

    [Fact]
    public void The_hook_command_hands_every_event_to_the_session_end_watch()
    {
        var command = Source("Cli", "Commands", "HookCommand.cs");

        Assert.Contains("ends.HandleAsync", command, StringComparison.Ordinal);
        Assert.Contains("Adapters.SessionEnds()", command, StringComparison.Ordinal);
    }

    [Fact]
    public void The_daemon_stop_marks_the_agents_closing_before_it_asks_the_daemon_to_shut_down()
    {
        var wiring = Source("Cli", "Composition", "EmbeddedWiring.cs");

        var marked = wiring.IndexOf("ClosingAgents.MarkAll", StringComparison.Ordinal);
        var shutdown = wiring.IndexOf("driver.ShutdownAsync", StringComparison.Ordinal);

        Assert.InRange(marked, 1, shutdown);
    }
}
