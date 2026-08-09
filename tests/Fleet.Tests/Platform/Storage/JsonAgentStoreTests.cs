using Fleet.Platform.Storage;
using Fleet.Ports.Agents.Models;

namespace Fleet.Tests.Platform.Storage;

[Collection(ConfigHomeCollection.Name)]
public sealed class JsonAgentStoreTests : ConfigHomeFixture
{
    private readonly JsonAgentStore _store = new();

    private static AgentRecord Agent(string worktree, string branch = "develop") =>
        new(worktree, "backend", branch, "claude", "origin/main", true);

    [Fact]
    public void A_project_with_no_session_file_has_no_agents()
    {
        Assert.Empty(_store.List("techweb"));
    }

    [Fact]
    public void A_saved_agent_round_trips()
    {
        _store.Save("techweb", Agent(Path.Combine(ConfigHome, "backend", "develop")));

        var agent = Assert.Single(_store.List("techweb"));

        Assert.Equal("backend", agent.Repository);
        Assert.Equal("develop", agent.Branch);
        Assert.Equal("claude", agent.Harness);
        Assert.True(agent.RepositoryWasBare);
    }

    [Fact]
    public void The_worktree_path_is_the_identity_so_saving_twice_updates_rather_than_duplicates()
    {
        var worktree = Path.Combine(ConfigHome, "backend", "develop");

        _store.Save("techweb", Agent(worktree));
        _store.Save("techweb", Agent(worktree, branch: "renamed"));

        var agent = Assert.Single(_store.List("techweb"));

        Assert.Equal("renamed", agent.Branch);
    }

    [Fact]
    public void Agents_do_not_leak_between_projects()
    {
        _store.Save("techweb", Agent(Path.Combine(ConfigHome, "backend", "develop")));

        Assert.Empty(_store.List("other"));
    }

    [Fact]
    public void Removing_an_agent_takes_it_out_of_the_list()
    {
        var worktree = Path.Combine(ConfigHome, "backend", "develop");

        _store.Save("techweb", Agent(worktree));
        _store.Remove("techweb", worktree);

        Assert.Empty(_store.List("techweb"));
    }

    [Fact]
    public void A_trailing_separator_still_identifies_the_same_agent()
    {
        var worktree = Path.Combine(ConfigHome, "backend", "develop");

        _store.Save("techweb", Agent(worktree));
        _store.Remove("techweb", worktree + Path.DirectorySeparatorChar);

        Assert.Empty(_store.List("techweb"));
    }

    [Fact]
    public void A_corrupt_session_file_reads_as_no_agents_rather_than_throwing()
    {
        FleetPaths.EnsureDirs();
        File.WriteAllText(Path.Combine(FleetPaths.Sessions, "techweb.json"), "{ not json");

        Assert.Empty(_store.List("techweb"));
    }
}
