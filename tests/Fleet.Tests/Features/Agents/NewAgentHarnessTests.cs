using Fleet.Features.Agents.NewAgent;
using Fleet.Features.Agents.NewAgent.Models;
using Fleet.Features.Repositories.AddRepository;
using Fleet.Features.Repositories.AddRepository.Models;
using Fleet.Platform.Git;
using Fleet.Platform.Mux.Fake;
using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;
using Fleet.Ports.Harness;
using Fleet.Shared.Constants;
using Fleet.Shared.Results;

namespace Fleet.Tests.Features.Agents;

public sealed class NewAgentHarnessTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("fleet-new-harness-").FullName;

    private readonly FakeMuxDriver _mux = new();

    public void Dispose()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(_root, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private async Task<string> RepositoryAsync()
    {
        var created = await new AddRepositoryHandler(new GitRunner())
            .HandleAsync(AddRepositoryCommand.CreateNew(_root, "backend", "main"));

        Assert.True(created.Succeeded, created.Error);
        return created.Value!.Path;
    }

    [Fact]
    public async Task A_new_opencode_agent_is_configured_before_its_pane_starts()
    {
        var directory = await RepositoryAsync();
        var configs = new RecordingConfigs(_mux);

        var result = await new NewAgentHandler(new GitRunner(), _mux, new NoStore(), configs: configs)
            .HandleAsync(new NewAgentCommand("techweb", "backend", directory, "feat", "", AgentHarness.Opencode));

        Assert.True(result.Succeeded, result.Error);
        var written = Assert.Single(configs.Written);
        Assert.Equal((AgentHarness.Opencode, result.Value!.Worktree, "techweb", "backend", "feat", 0), written);
        Assert.Single(await _mux.ListPanesAsync());
    }

    private sealed class RecordingConfigs(FakeMuxDriver mux) : IHarnessConfigs, IHarnessConfig
    {
        private string _harness = string.Empty;

        public List<(string, string, string, string, string, int)> Written { get; } = [];

        public IHarnessConfig For(string harness)
        {
            _harness = harness;
            return this;
        }

        public Result WriteForOrchestration(string folder, string project, string caller) => Result.Ok();

        public Result WriteForAgent(string folder, string project, string repository, string branch)
        {
            var panes = mux.ListPanesAsync().GetAwaiter().GetResult().Count;
            Written.Add((_harness, folder, project, repository, branch, panes));
            return Result.Ok();
        }
    }

    private sealed class NoStore : IAgentStore
    {
        private readonly List<AgentRecord> _agents = [];

        public void Save(string project, AgentRecord agent) => _agents.Add(agent);

        public bool Replace(string project, AgentRecord agent) => true;

        public IReadOnlyList<AgentRecord> List(string project) => _agents;

        public bool Remove(string project, string worktree) => true;
    }
}
