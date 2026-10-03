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
    public void Hidden_and_open_survive_the_round_trip_because_that_is_the_session_state()
    {
        var worktree = Path.Combine(ConfigHome, "backend", "develop");

        _store.Save("techweb", Agent(worktree) with { Hidden = true, Open = true });

        var agent = Assert.Single(_store.List("techweb"));

        Assert.True(agent.Hidden);
        Assert.True(agent.Open);

        _store.Save("techweb", agent with { Open = false });

        Assert.False(Assert.Single(_store.List("techweb")).Open);
    }

    [Fact]
    public void The_owner_and_status_of_an_orchestration_survive_the_round_trip()
    {
        var worktree = Path.Combine(ConfigHome, ".fleet", "orchestrations", "add-login");

        _store.Save(
            "techweb",
            Agent(worktree) with { Owner = "parent", Status = "done" });

        var agent = Assert.Single(_store.List("techweb"));

        Assert.Equal("parent", agent.Owner);
        Assert.Equal("done", agent.Status);
    }

    [Fact]
    public void A_session_written_before_these_fields_existed_loads_with_empty_defaults()
    {
        FleetPaths.EnsureDirs();
        File.WriteAllText(
            Path.Combine(FleetPaths.Sessions, "techweb.json"),
            """{"version":1,"agents":[{"worktree":"~/x","repository":"backend","branch":"dev","harness":"claude"}]}""");

        var agent = Assert.Single(_store.List("techweb"));

        Assert.Equal(string.Empty, agent.Owner);
        Assert.Equal(string.Empty, agent.Status);
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

    [Fact]
    public void A_save_waits_for_a_session_file_someone_else_holds_open()
    {
        _store.Save("techweb", Agent(Path.Combine(ConfigHome, "backend", "first"), "first"));
        var file = Path.Combine(FleetPaths.Sessions, "techweb.json");

        using (HeldShut.For(file, TimeSpan.FromMilliseconds(300)))
        {
            _store.Save("techweb", Agent(Path.Combine(ConfigHome, "backend", "second"), "second"));
        }

        Assert.Equal(["first", "second"], _store.List("techweb").Select(a => a.Branch));
    }

    [Fact]
    public async Task A_session_file_that_stays_unreadable_is_left_alone_rather_than_overwritten()
    {
        _store.Save("techweb", Agent(Path.Combine(ConfigHome, "backend", "first"), "first"));
        _store.Save("techweb", Agent(Path.Combine(ConfigHome, "backend", "second"), "second"));
        var file = Path.Combine(FleetPaths.Sessions, "techweb.json");

        // Readers are shut out. On Unix the replace (a rename) still succeeds, so the old store,
        // which read "nothing" after 45 ms, wrote that over both records; on Windows the replace
        // failed too, so there this guards the skip rather than reproducing the wipe.
        using (new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            await Task.Run(() => _store.Save("techweb", Agent(Path.Combine(ConfigHome, "backend", "third"), "third")));
        }

        Assert.Equal(["first", "second"], _store.List("techweb").Select(a => a.Branch));
    }

    [Fact]
    public void Concurrent_writers_from_separate_stores_do_not_lose_each_others_records()
    {
        const int writers = 24;

        Parallel.For(0, writers, i =>
            new JsonAgentStore().Save(
                "techweb", Agent(Path.Combine(ConfigHome, "backend", $"agent-{i}"), $"branch-{i}")));

        Assert.Equal(writers, _store.List("techweb").Count);
    }

    [Fact]
    public void The_host_an_orchestrator_was_started_in_survives_the_round_trip()
    {
        var bare = Path.Combine(ConfigHome, "orchestrations", "bare");
        var nvim = Path.Combine(ConfigHome, "orchestrations", "nvim");

        _store.Save("techweb", Agent(bare, "bare") with { Harness = "orchestrator", InNvim = false });
        _store.Save("techweb", Agent(nvim, "nvim") with { Harness = "orchestrator", InNvim = true });

        var agents = _store.List("techweb");

        Assert.False(agents.Single(a => a.Branch == "bare").StartedInNvim);
        Assert.True(agents.Single(a => a.Branch == "nvim").StartedInNvim);
    }

    [Fact]
    public void An_agent_saved_without_a_host_reads_back_as_nvim()
    {
        _store.Save("techweb", Agent(Path.Combine(ConfigHome, "backend", "develop")));

        var agent = Assert.Single(_store.List("techweb"));

        Assert.Null(agent.InNvim);
        Assert.True(agent.StartedInNvim);
    }
}
