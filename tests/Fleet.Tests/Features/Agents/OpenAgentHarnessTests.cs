using Fleet.Features.Agents.OpenAgent;
using Fleet.Platform.Mux.Fake;
using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;
using Fleet.Ports.Harness;
using Fleet.Shared.Constants;
using Fleet.Shared.Results;

namespace Fleet.Tests.Features.Agents;

public sealed class OpenAgentHarnessTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("fleet-open-harness-").FullName;

    private readonly FakeMuxDriver _mux = new();

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private AgentRecord Agent(string harness)
    {
        var worktree = Path.Combine(_root, "api_feat");
        Directory.CreateDirectory(worktree);

        return new AgentRecord(worktree, "api", "feat", harness, "origin/main", true);
    }

    [Fact]
    public async Task A_reopened_opencode_agent_is_configured_and_resumed()
    {
        var configs = new RecordingConfigs();
        var agent = Agent(AgentHarness.Opencode);

        var opened = await new OpenAgentHandler(_mux, new NoStore(), configs: configs)
            .HandleAsync("proj", agent, _root);

        Assert.True(opened.Succeeded);
        Assert.Equal([(AgentHarness.Opencode, agent.Worktree, "proj", "api", "feat")], configs.Written);
        var pane = Assert.Single(await _mux.ListPanesAsync());
        Assert.Equal(["opencode", "--continue"], _mux.ArgsFor(pane.Id));
    }

    [Theory]
    [InlineData("codex", new[] { "codex", "resume", "--last" })]
    [InlineData("pi", new[] { "pi", "--continue" })]
    public async Task A_reopened_agent_of_another_harness_is_configured_and_resumed(string harness, string[] resumed)
    {
        var configs = new RecordingConfigs();
        var agent = Agent(harness);

        var opened = await new OpenAgentHandler(_mux, new NoStore(), configs: configs)
            .HandleAsync("proj", agent, _root);

        Assert.True(opened.Succeeded, opened.Error);
        Assert.Equal([(harness, agent.Worktree, "proj", "api", "feat")], configs.Written);
        Assert.Equal(resumed, _mux.ArgsFor(Assert.Single(await _mux.ListPanesAsync()).Id));
    }

    [Fact]
    public async Task A_reopened_opencode_agent_hosted_in_nvim_resumes_inside_an_nvim_terminal()
    {
        var configs = new RecordingConfigs();
        var agent = Agent(AgentHarness.Opencode) with { HostInNvim = true };

        var opened = await new OpenAgentHandler(_mux, new NoStore(), configs: configs)
            .HandleAsync("proj", agent, _root);

        Assert.True(opened.Succeeded);
        Assert.Equal([(AgentHarness.Opencode, agent.Worktree, "proj", "api", "feat")], configs.Written);
        var args = _mux.ArgsFor(Assert.Single(await _mux.ListPanesAsync()).Id);
        Assert.Equal(AgentHarness.Nvim, args[0]);
        Assert.Contains("local fleet_cmd={'opencode','--continue'}", args[2], StringComparison.Ordinal);
        Assert.Empty(_mux.EnvFor(Assert.Single(await _mux.ListPanesAsync()).Id));
    }

    [Theory]
    [InlineData("opencode", new[] { "opencode", "--continue" })]
    [InlineData("codex", new[] { "codex", "resume", "--last" })]
    [InlineData("pi", new[] { "pi", "--continue" })]
    public async Task A_reopened_sub_orchestrator_of_another_harness_resumes_with_its_profile(
        string harness, string[] resumed)
    {
        var sub = Agent(AgentHarness.Orchestrator) with { Repository = string.Empty, OrchestratorHarness = harness };

        var opened = await new OpenAgentHandler(_mux, new NoStore(), subOrchestratorsInNvim: false)
            .HandleAsync("proj", sub, _root);

        Assert.True(opened.Succeeded, opened.Error);
        var pane = Assert.Single(await _mux.ListPanesAsync());
        Assert.Equal(resumed, _mux.ArgsFor(pane.Id));
        Assert.Empty(_mux.EnvFor(pane.Id));
        Assert.Equal(sub.Worktree, pane.Cwd);
    }

    [Fact]
    public async Task A_reopened_opencode_sub_orchestrator_hosted_in_nvim_resumes_inside_an_nvim_terminal()
    {
        var sub = Agent(AgentHarness.Orchestrator) with { Repository = string.Empty, OrchestratorHarness = "opencode" };

        await new OpenAgentHandler(_mux, new NoStore(), subOrchestratorsInNvim: true).HandleAsync("proj", sub, _root);

        var pane = Assert.Single(await _mux.ListPanesAsync());
        Assert.Equal(AgentHarness.Nvim, _mux.ArgsFor(pane.Id)[0]);
        Assert.Contains("local fleet_cmd={'opencode','--continue'}", _mux.ArgsFor(pane.Id)[2], StringComparison.Ordinal);
        Assert.Empty(_mux.EnvFor(pane.Id));
    }

    private sealed class RecordingConfigs : IHarnessConfigs, IHarnessConfig
    {
        private string _harness = string.Empty;

        public List<(string, string, string, string, string)> Written { get; } = [];

        public IHarnessConfig For(string harness)
        {
            _harness = harness;
            return this;
        }

        public Result WriteForOrchestration(string folder, string project, string caller) => Result.Ok();

        public Result WriteForAgent(string folder, string project, string repository, string branch)
        {
            Written.Add((_harness, folder, project, repository, branch));
            return Result.Ok();
        }
    }

    private sealed class NoStore : IAgentStore
    {
        public void Save(string project, AgentRecord agent)
        {
        }

        public bool Replace(string project, AgentRecord agent) => true;

        public IReadOnlyList<AgentRecord> List(string project) => [];

        public bool Remove(string project, string worktree) => true;
    }
}
