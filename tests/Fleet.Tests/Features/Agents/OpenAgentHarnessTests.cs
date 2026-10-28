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
