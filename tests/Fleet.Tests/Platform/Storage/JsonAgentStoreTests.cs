using Fleet.Platform.Storage;
using Fleet.Ports.Agents.Models;
using Fleet.Shared.Evidence;
using Fleet.Shared.Settings.Models;

namespace Fleet.Tests.Platform.Storage;

public sealed class JsonAgentStoreTests : ConfigHomeFixture
{
    private static readonly TimeSpan ShortPatience = TimeSpan.FromMilliseconds(150);

    private readonly JsonAgentStore _store = new();

    private readonly JsonAgentStore _impatient = new(lockPatience: ShortPatience, busyPatience: ShortPatience);

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
    public void The_evidence_of_a_report_survives_the_round_trip()
    {
        var worktree = Path.Combine(ConfigHome, "backend", "develop");
        var evidence = new ReportEvidence(
            worktree, "origin/main", "1111111111111111111111111111111111111111",
            "2222222222222222222222222222222222222222", true, "dotnet test", 3, "1 failed\nline 2");

        _store.Save("techweb", Agent(worktree) with { Status = "done", Evidence = evidence });

        Assert.Equal(evidence, Assert.Single(_store.List("techweb")).Evidence);
    }

    [Fact]
    public void Evidence_without_verify_fields_round_trips_with_them_empty()
    {
        var worktree = Path.Combine(ConfigHome, "backend", "develop");
        var evidence = new ReportEvidence(worktree, "main", "aaa", "bbb", false);

        _store.Save("techweb", Agent(worktree) with { Evidence = evidence });

        var loaded = Assert.Single(_store.List("techweb")).Evidence!;
        Assert.Equal(evidence, loaded);
        Assert.Null(loaded.VerifyExitCode);
        Assert.Equal(Verification.Unverified, loaded.Verification);
    }

    [Fact]
    public void A_zero_exit_code_is_kept_and_is_not_mistaken_for_none()
    {
        var worktree = Path.Combine(ConfigHome, "backend", "develop");

        _store.Save("techweb", Agent(worktree) with { Evidence = new ReportEvidence(worktree, "main", "a", "b", false, "t", 0) });

        Assert.Equal(0, Assert.Single(_store.List("techweb")).Evidence!.VerifyExitCode);
    }

    [Fact]
    public void The_worktree_inside_the_evidence_is_stored_with_the_home_contracted_like_the_agents_own()
    {
        var worktree = Path.Combine(ConfigHome, "backend", "develop");

        _store.Save("techweb", Agent(worktree) with { Evidence = new ReportEvidence(worktree, "main", "a", "b", false) });

        Assert.Equal(worktree, Assert.Single(_store.List("techweb")).Evidence!.Worktree);
    }

    [Fact]
    public void A_session_written_before_evidence_existed_loads_with_none()
    {
        FleetPaths.EnsureDirs();
        File.WriteAllText(
            Path.Combine(FleetPaths.Sessions, "techweb.json"),
            """{"version":1,"agents":[{"worktree":"~/x","repository":"backend","branch":"dev","harness":"claude","status":"done","summary":"s","reportedAt":"2026-11-15T10:00:00.0000000+00:00"}]}""");

        var agent = Assert.Single(_store.List("techweb"));

        Assert.Null(agent.Evidence);
        Assert.Equal("done", agent.Status);
        Assert.Equal("done (unverified)", agent.ReportLabel);
    }

    [Fact]
    public void An_agent_that_never_reported_leaves_no_evidence_in_the_file()
    {
        _store.Save("techweb", Agent(Path.Combine(ConfigHome, "backend", "develop")));

        Assert.DoesNotContain("evidence", File.ReadAllText(Path.Combine(FleetPaths.Sessions, "techweb.json")));
    }

    [Fact]
    public void Updating_an_agent_keeps_its_evidence()
    {
        var worktree = Path.Combine(ConfigHome, "backend", "develop");
        var evidence = new ReportEvidence(worktree, "main", "a", "b", false, "t", 0, "ok");
        _store.Save("techweb", Agent(worktree) with { Evidence = evidence });

        _store.Update("techweb", worktree, a => a with { Hidden = true });

        Assert.Equal(evidence, Assert.Single(_store.List("techweb")).Evidence);
    }

    [Fact]
    public void A_routed_model_and_effort_survive_the_round_trip()
    {
        var worktree = Path.Combine(ConfigHome, ".fleet", "orchestrations", "add-login");

        _store.Save("techweb", Agent(worktree) with { Model = new RoleModel("opus", "high") });

        Assert.Equal(new RoleModel("opus", "high"), Assert.Single(_store.List("techweb")).Model);
    }

    [Fact]
    public void An_agent_without_a_routed_model_stores_none()
    {
        _store.Save("techweb", Agent(Path.Combine(ConfigHome, "backend", "develop")));

        Assert.Null(Assert.Single(_store.List("techweb")).Model);
    }

    [Fact]
    public void A_session_written_before_routing_loads_with_no_model()
    {
        FleetPaths.EnsureDirs();
        File.WriteAllText(
            Path.Combine(FleetPaths.Sessions, "techweb.json"),
            """{"version":1,"agents":[{"worktree":"~/x","repository":"","branch":"sub","harness":"orchestrator"}]}""");

        Assert.Null(Assert.Single(_store.List("techweb")).Model);
    }

    [Fact]
    public void The_time_an_agent_was_last_instructed_survives_the_round_trip()
    {
        _store.Save(
            "techweb",
            Agent(Path.Combine(ConfigHome, "backend", "develop")) with { InstructedAt = "2026-11-15T10:00:00.0000000+00:00" });

        Assert.Equal("2026-11-15T10:00:00.0000000+00:00", Assert.Single(_store.List("techweb")).InstructedAt);
    }

    [Fact]
    public void A_session_written_before_instructions_were_timed_loads_with_no_instruction_time()
    {
        FleetPaths.EnsureDirs();
        File.WriteAllText(
            Path.Combine(FleetPaths.Sessions, "techweb.json"),
            """{"version":1,"agents":[{"worktree":"~/x","repository":"backend","branch":"dev","harness":"claude"}]}""");

        Assert.Equal(string.Empty, Assert.Single(_store.List("techweb")).InstructedAt);
    }

    [Fact]
    public void An_agent_never_instructed_leaves_no_instruction_time_in_the_file()
    {
        _store.Save("techweb", Agent(Path.Combine(ConfigHome, "backend", "develop")));

        Assert.DoesNotContain("instructedAt", File.ReadAllText(Path.Combine(FleetPaths.Sessions, "techweb.json")), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Update_changes_the_stored_record_in_one_locked_step()
    {
        var worktree = Path.Combine(ConfigHome, "backend", "develop");
        _store.Save("techweb", Agent(worktree) with { Status = "done" });

        var updated = _store.Update("techweb", worktree, a => a with { Status = "working", InstructedAt = "t1" });

        Assert.True(updated);
        var agent = Assert.Single(_store.List("techweb"));
        Assert.Equal("working", agent.Status);
        Assert.Equal("t1", agent.InstructedAt);
    }

    [Fact]
    public void Update_of_an_agent_that_is_gone_changes_nothing()
    {
        var worktree = Path.Combine(ConfigHome, "backend", "develop");

        Assert.False(_store.Update("techweb", worktree, a => a with { Status = "working" }));
        Assert.Empty(_store.List("techweb"));
    }

    [Fact]
    public void Concurrent_updates_of_different_fields_do_not_lose_each_other()
    {
        var worktree = Path.Combine(ConfigHome, "backend", "develop");
        _store.Save("techweb", Agent(worktree));

        Parallel.For(0, 20, i =>
        {
            new JsonAgentStore().Update("techweb", worktree, a => i % 2 == 0
                ? a with { Summary = a.Summary + "x" }
                : a with { ReportedAt = a.ReportedAt + "y" });
        });

        var agent = Assert.Single(_store.List("techweb"));
        Assert.Equal(10, agent.Summary.Length);
        Assert.Equal(10, agent.ReportedAt.Length);
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
            await Task.Run(() => _impatient.Save("techweb", Agent(Path.Combine(ConfigHome, "backend", "third"), "third")));
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
    [Fact]
    public void Replacing_a_removed_agent_does_not_bring_it_back()
    {
        var worktree = Path.Combine(ConfigHome, ".fleet", "orchestrations", "upgrade");
        var stale = Agent(worktree) with { Harness = "orchestrator", Open = true };

        _store.Save("techweb", stale);
        Assert.True(_store.Remove("techweb", worktree));

        Assert.False(_store.Replace("techweb", stale with { Open = false }));
        Assert.Empty(_store.List("techweb"));
    }

    [Fact]
    public void Replacing_an_existing_agent_updates_it()
    {
        var worktree = Path.Combine(ConfigHome, "backend", "develop");

        _store.Save("techweb", Agent(worktree));

        Assert.True(_store.Replace("techweb", Agent(worktree) with { Open = true }));
        Assert.True(Assert.Single(_store.List("techweb")).Open);
    }

    [Fact]
    public async Task Removing_from_a_session_file_that_stays_unreadable_reports_failure()
    {
        var worktree = Path.Combine(ConfigHome, "backend", "develop");
        _store.Save("techweb", Agent(worktree));
        var file = Path.Combine(FleetPaths.Sessions, "techweb.json");

        bool removed;

        using (new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            removed = await Task.Run(() => _impatient.Remove("techweb", worktree));
        }

        Assert.False(removed);
        Assert.Single(_store.List("techweb"));
    }

    [Fact]
    public async Task A_new_agent_is_still_saved_while_the_session_lock_stays_held()
    {
        FleetPaths.EnsureDirs();
        var lockFile = Path.Combine(FleetPaths.Sessions, "techweb.json.lock");

        using (new FileStream(lockFile, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            await Task.Run(() => _impatient.Save("techweb", Agent(Path.Combine(ConfigHome, "backend", "develop"))));
        }

        Assert.Single(_store.List("techweb"));
    }

    [Fact]
    public async Task Removing_while_the_session_lock_stays_held_reports_failure()
    {
        var worktree = Path.Combine(ConfigHome, "backend", "develop");
        _store.Save("techweb", Agent(worktree));
        var lockFile = Path.Combine(FleetPaths.Sessions, "techweb.json.lock");

        bool removed;

        using (new FileStream(lockFile, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            removed = await Task.Run(() => _impatient.Remove("techweb", worktree));
        }

        Assert.False(removed);
        Assert.Single(_store.List("techweb"));
    }

    private static string Legacy(string entry) =>
        $$"""{"version":1,"agents":[{"worktree":"~/x","repository":"backend","branch":"dev",{{entry}}}]}""";

    private AgentRecord LoadLegacy(string entry)
    {
        FleetPaths.EnsureDirs();
        File.WriteAllText(Path.Combine(FleetPaths.Sessions, "techweb.json"), Legacy(entry));

        return Assert.Single(_store.List("techweb"));
    }

    [Fact]
    public void A_legacy_nvim_agent_that_ran_claude_loads_as_claude_hosted_in_nvim()
    {
        var agent = LoadLegacy("\"harness\":\"nvim\",\"claude\":true");

        Assert.Equal("claude", agent.Harness);
        Assert.True(agent.HostInNvim);
    }

    [Fact]
    public void A_legacy_owned_nvim_agent_without_a_claude_flag_loads_as_claude_hosted_in_nvim()
    {
        var agent = LoadLegacy("\"harness\":\"nvim\",\"owner\":\"add-login\"");

        Assert.Equal("claude", agent.Harness);
        Assert.True(agent.HostInNvim);
    }

    [Fact]
    public void A_legacy_plain_nvim_agent_stays_an_editor_without_an_ai()
    {
        var agent = LoadLegacy("\"harness\":\"nvim\",\"claude\":false");

        Assert.Equal("nvim", agent.Harness);
        Assert.False(agent.HostInNvim);
    }

    [Fact]
    public void A_legacy_opencode_agent_marked_as_claude_loads_as_plain_opencode()
    {
        var agent = LoadLegacy("\"harness\":\"opencode\",\"claude\":true");

        Assert.Equal("opencode", agent.Harness);
        Assert.False(agent.HostInNvim);
    }

    [Fact]
    public void A_legacy_claude_agent_stays_claude_outside_nvim()
    {
        var agent = LoadLegacy("\"harness\":\"claude\",\"claude\":true");

        Assert.Equal("claude", agent.Harness);
        Assert.False(agent.HostInNvim);
    }

    [Fact]
    public void A_migrated_agent_is_written_back_without_the_legacy_claude_flag()
    {
        var agent = LoadLegacy("\"harness\":\"nvim\",\"claude\":true");

        _store.Save("techweb", agent with { Status = "done" });

        var json = File.ReadAllText(Path.Combine(FleetPaths.Sessions, "techweb.json"));
        Assert.DoesNotContain("\"claude\":", json, StringComparison.Ordinal);
        Assert.Contains("\"hostInNvim\": true", json, StringComparison.Ordinal);
        Assert.Equal(agent with { Status = "done" }, Assert.Single(_store.List("techweb")));
    }

    [Theory]
    [InlineData("claude", true)]
    [InlineData("claude", false)]
    [InlineData("opencode", true)]
    [InlineData("opencode", false)]
    [InlineData("nvim", false)]
    public void The_harness_and_the_nvim_host_round_trip_independently(string harness, bool host)
    {
        _store.Save("techweb", Agent(Path.Combine(ConfigHome, "backend", "develop")) with { Harness = harness, HostInNvim = host });

        var agent = Assert.Single(_store.List("techweb"));

        Assert.Equal(harness, agent.Harness);
        Assert.Equal(host, agent.HostInNvim);
    }

    [Fact]
    public void A_sub_orchestrators_harness_round_trips_and_defaults_to_claude()
    {
        var folder = Path.Combine(ConfigHome, ".fleet", "orchestrations", "add-login");

        _store.Save("techweb", Agent(folder) with { Harness = "orchestrator" });
        Assert.Equal("claude", Assert.Single(_store.List("techweb")).OrchestratorProfile.Name);

        _store.Save("techweb", Agent(folder) with { Harness = "orchestrator", OrchestratorHarness = "opencode" });
        var sub = Assert.Single(_store.List("techweb"));
        Assert.Equal("opencode", sub.OrchestratorHarness);
        Assert.Equal("opencode", sub.OrchestratorProfile.Name);
    }
}

public sealed class FindByWorktreeTests : ConfigHomeFixture
{
    private readonly JsonAgentStore _store = new();

    private static AgentRecord Agent(string worktree) =>
        new(worktree, "backend", "develop", "claude", "origin/main", true, Open: true);

    [Fact]
    public void An_agent_is_found_across_projects_with_the_project_that_holds_it()
    {
        var mine = Path.Combine(ConfigHome, "backend", "mine");
        _store.Save("alpha", Agent(Path.Combine(ConfigHome, "backend", "other")));
        _store.Save("beta", Agent(mine));

        var found = _store.FindByWorktree(mine);

        Assert.NotNull(found);
        Assert.Equal("beta", found.SessionKey);
        Assert.Equal(mine, found.Record.Worktree);
    }

    [Fact]
    public void A_cwd_below_the_worktree_finds_it_and_the_deepest_match_wins()
    {
        var outer = Path.Combine(ConfigHome, "backend", "mine");
        var inner = Path.Combine(outer, ".fleet", "orchestrations", "lead");
        _store.Save("alpha", Agent(outer));
        _store.Save("alpha", Agent(inner) with { Branch = "lead" });

        Assert.Equal("develop", _store.FindByWorktree(Path.Combine(outer, "src", "deep"))!.Record.Branch);
        Assert.Equal("lead", _store.FindByWorktree(Path.Combine(inner, "src"))!.Record.Branch);
        Assert.Null(_store.FindByWorktree(Path.Combine(ConfigHome, "backend", "mine-other")));
    }

    [Fact]
    public void The_session_key_is_the_sanitized_project_name_the_file_is_stored_under()
    {
        var mine = Path.Combine(ConfigHome, "backend", "mine");
        _store.Save("my project!", Agent(mine));

        Assert.Equal("myproject", _store.FindByWorktree(mine)!.SessionKey);
    }

    [Fact]
    public void A_worktree_no_project_has_is_not_found()
    {
        _store.Save("alpha", Agent(Path.Combine(ConfigHome, "backend", "other")));

        Assert.Null(_store.FindByWorktree(Path.Combine(ConfigHome, "backend", "missing")));
    }

    [Fact]
    public void With_no_sessions_folder_nothing_is_found()
    {
        Assert.Null(_store.FindByWorktree(Path.Combine(ConfigHome, "backend", "mine")));
    }
}
