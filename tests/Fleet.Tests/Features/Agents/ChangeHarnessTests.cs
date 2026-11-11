using Fleet.Features.Agents.ChangeHarness;
using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;
using Fleet.Shared.Constants;

namespace Fleet.Tests.Features.Agents;

public sealed class ChangeHarnessTests
{
    private static readonly AgentRecord Agent = new("/w", "api", "feat/x", AgentHarness.Claude, "main", false);

    [Fact]
    public void An_unregistered_harness_is_refused_and_nothing_is_stored()
    {
        var store = new RecordingStore();

        var changed = new ChangeHarnessHandler(store).Handle("p", Agent, "codexx");

        Assert.False(changed.Succeeded);
        Assert.Contains("nvim, claude, opencode", changed.Error);
        Assert.Empty(store.Replaced);
    }

    [Fact]
    public void A_registered_harness_is_stored()
    {
        var store = new RecordingStore();

        var changed = new ChangeHarnessHandler(store).Handle("p", Agent, " opencode ");

        Assert.True(changed.Succeeded);
        Assert.Equal(AgentHarness.Opencode, Assert.Single(store.Replaced).Harness);
    }

    [Theory]
    [InlineData("opencode", true, "opencode", true)]
    [InlineData("claude", false, "claude", false)]
    [InlineData("nvim", true, "nvim", false)]
    public void The_harness_and_the_nvim_host_are_chosen_together_but_stored_apart(
        string harness, bool host, string expected, bool expectedHost)
    {
        var store = new RecordingStore();

        var changed = new ChangeHarnessHandler(store).Handle("p", Agent with { HostInNvim = true }, harness, host);

        Assert.True(changed.Succeeded);
        Assert.Equal(expected, changed.Value!.Harness);
        Assert.Equal(expectedHost, changed.Value!.HostInNvim);
    }

    [Fact]
    public void Switching_only_the_host_keeps_the_harness()
    {
        var store = new RecordingStore();

        var changed = new ChangeHarnessHandler(store).Handle("p", Agent with { Harness = AgentHarness.Opencode }, AgentHarness.Opencode, hostInNvim: true);

        Assert.Equal(AgentHarness.Opencode, Assert.Single(store.Replaced).Harness);
        Assert.True(changed.Value!.HostInNvim);
    }

    [Fact]
    public void Asking_for_nvim_without_a_host_choice_hosts_the_agents_harness_in_nvim()
    {
        var store = new RecordingStore();

        var changed = new ChangeHarnessHandler(store).Handle("p", Agent with { Harness = AgentHarness.Opencode }, AgentHarness.Nvim);

        Assert.Equal(AgentHarness.Opencode, changed.Value!.Harness);
        Assert.True(changed.Value!.HostInNvim);
    }

    [Fact]
    public void Asking_for_nvim_on_an_editor_only_agent_changes_nothing()
    {
        var store = new RecordingStore();

        var changed = new ChangeHarnessHandler(store).Handle("p", Agent with { Harness = AgentHarness.Nvim }, AgentHarness.Nvim);

        Assert.Equal(AgentHarness.Nvim, changed.Value!.Harness);
        Assert.Empty(store.Replaced);
    }

    [Fact]
    public void Asking_for_a_harness_by_name_alone_runs_it_outside_nvim()
    {
        var store = new RecordingStore();

        var changed = new ChangeHarnessHandler(store).Handle("p", Agent with { HostInNvim = true }, AgentHarness.Opencode);

        Assert.Equal(AgentHarness.Opencode, changed.Value!.Harness);
        Assert.False(changed.Value!.HostInNvim);
    }

    private sealed class RecordingStore : IAgentStore
    {
        public List<AgentRecord> Replaced { get; } = [];

        public void Save(string project, AgentRecord agent) => Replaced.Add(agent);

        public IReadOnlyList<AgentRecord> List(string project) => Replaced;

        public bool Replace(string project, AgentRecord agent)
        {
            Replaced.Add(agent);
            return true;
        }

        public bool Remove(string project, string worktree) => true;
    }
}
