using Fleet.Cli.Composition;
using Fleet.Platform.Mux.Fake;
using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;
using Fleet.Ports.Projects;
using Fleet.Ports.Projects.Models;
using Fleet.Shared.Constants;
using Fleet.Shared.Status;

namespace Fleet.Tests.Cli;

public sealed class ClosingAgentsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fleet-tests", Path.GetRandomFileName());

    public ClosingAgentsTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public void Stopping_the_daemon_marks_every_open_agent_of_every_project_and_no_closed_one()
    {
        var open = Record("open", isOpen: true);
        var shut = Record("shut", isOpen: false);
        var other = Record("other", isOpen: true);

        ClosingAgents.MarkAll(
            new Projects(["alpha", "beta"]),
            new Agents(new Dictionary<string, AgentRecord[]> { ["alpha"] = [open, shut], ["beta"] = [other] }));

        Assert.True(ClosingMark.Exists(open.Worktree));
        Assert.False(ClosingMark.Exists(shut.Worktree));
        Assert.True(ClosingMark.Exists(other.Worktree));
    }

    [Fact]
    public async Task Closing_a_project_from_the_head_marks_its_open_agents_first_and_no_other_project()
    {
        var open = Record("open", isOpen: true);
        var shut = Record("shut", isOpen: false);
        var other = Record("other", isOpen: true);
        var agents = new Agents(new Dictionary<string, AgentRecord[]> { ["alpha"] = [open, shut], ["beta"] = [other] });
        var mux = new FakeMuxDriver(workspaces: true);

        var problem = await HeadWiring.CloseProjectAsync(mux, agents, new Project("alpha", _root), CancellationToken.None);

        Assert.Null(problem);
        Assert.True(ClosingMark.Exists(open.Worktree));
        Assert.False(ClosingMark.Exists(shut.Worktree));
        Assert.False(ClosingMark.Exists(other.Worktree));
    }

    [Fact]
    public async Task A_terminal_that_cannot_close_a_workspace_marks_nobody()
    {
        var open = Record("open", isOpen: true);
        var agents = new Agents(new Dictionary<string, AgentRecord[]> { ["alpha"] = [open] });

        var problem = await HeadWiring.CloseProjectAsync(new FakeMuxDriver(), agents, new Project("alpha", _root), CancellationToken.None);

        Assert.NotNull(problem);
        Assert.False(ClosingMark.Exists(open.Worktree));
    }

    private AgentRecord Record(string branch, bool isOpen)
    {
        var worktree = Path.Combine(_root, branch);
        Directory.CreateDirectory(worktree);

        return new AgentRecord(worktree, "api", branch, AgentHarness.Claude, "origin/main", true, Open: isOpen);
    }

    private sealed class Projects(string[] names) : IProjectStore
    {
        public Project? Load(string name) => null;

        public IReadOnlyList<Project> List() => [.. names.Select(n => new Project(n, "C:/" + n))];

        public void Save(Project project)
        {
        }

        public void Remove(string name)
        {
        }
    }

    private sealed class Agents(Dictionary<string, AgentRecord[]> byProject) : IAgentStore
    {
        public IReadOnlyList<AgentRecord> List(string project) => byProject.GetValueOrDefault(project) ?? [];

        public void Save(string project, AgentRecord agent)
        {
        }

        public bool Replace(string project, AgentRecord agent) => true;

        public bool Update(string project, string worktree, Func<AgentRecord, AgentRecord> change) => true;

        public bool Remove(string project, string worktree) => true;
    }
}
