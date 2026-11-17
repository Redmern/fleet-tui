using Fleet.Features.Orchestrations.ReportStatus;
using Fleet.Features.Orchestrations.ReportStatus.Models;
using Fleet.Platform.Git;
using Fleet.Ports.Agents.Models;
using Fleet.Shared.Constants;
using Fleet.Shared.Evidence;
using Fleet.Tests.EndToEnd.MixedTeam;

namespace Fleet.Tests.Features.Orchestrations;

// AC-9, AC-10, AC-11: the git state a report records, against real git.
public sealed class ReportEvidenceTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "fleet-tests", Path.GetRandomFileName());

    private readonly GitRunner _git = new();

    private readonly MixedTeamRig.MemoryStore _store = new();

    private string _repo = string.Empty;

    private string _mainSha = string.Empty;

    public ReportEvidenceTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, File.GetAttributes(file) & ~FileAttributes.ReadOnly);
            }

            Directory.Delete(_root, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private async Task<string> GitAsync(string directory, params string[] args)
    {
        var result = await _git.RunAsync(directory, ["-c", "user.name=t", "-c", "user.email=t@t", .. args]);

        Assert.True(result.Ok, $"git {string.Join(' ', args)}: {result.Message}");

        return result.Out.Trim();
    }

    private async Task<AgentRecord> AgentOnFeatureBranchAsync(int commits = 2, string harness = AgentHarness.Claude)
    {
        _repo = Path.Combine(_root, "api");
        Directory.CreateDirectory(_repo);
        await GitAsync(_repo, "init", "-b", "main");
        await GitAsync(_repo, "commit", "--allow-empty", "-m", "base");
        _mainSha = await GitAsync(_repo, "rev-parse", "HEAD");
        await GitAsync(_repo, "checkout", "-b", "feat/x");

        for (var i = 0; i < commits; i++)
        {
            await GitAsync(_repo, "commit", "--allow-empty", "-m", $"work {i}");
        }

        var record = new AgentRecord(_repo, "api", "feat/x", harness, "main", false);
        _store.Save("techweb", record);

        return record;
    }

    private AgentRecord Stored(string worktree) =>
        _store.List("techweb").Single(a => a.Worktree == worktree);

    private ReportStatusHandler Handler(Func<OwnerNotice, CancellationToken, Task<bool>>? typeToOwner = null) =>
        new(_store, git: _git, typeToOwner: typeToOwner);

    private Task<Fleet.Shared.Results.Result<string>> ReportAsync(
        string status, string summary = "s", VerifyReport? verify = null) =>
        Handler().HandleAsync("techweb", _root, "agent:api/feat/x", status, summary, verify: verify);

    [Fact]
    public async Task Any_report_records_the_worktree_the_base_the_merge_base_the_head_and_a_clean_tree()
    {
        var agent = await AgentOnFeatureBranchAsync();
        var head = await GitAsync(_repo, "rev-parse", "HEAD");

        var result = await ReportAsync("working");

        Assert.True(result.Succeeded, result.Error);
        var evidence = Assert.IsType<ReportEvidence>(Stored(agent.Worktree).Evidence);
        Assert.Equal(Path.GetFullPath(_repo), Path.GetFullPath(evidence.Worktree));
        Assert.Equal("main", evidence.BaseRef);
        Assert.Equal(_mainSha, evidence.MergeBase);
        Assert.Equal(head, evidence.Head);
        Assert.NotEqual(evidence.MergeBase, evidence.Head);
        Assert.False(evidence.Dirty);
    }

    [Theory]
    [InlineData("working")]
    [InlineData("done")]
    [InlineData("failed")]
    public async Task Every_status_records_evidence(string status)
    {
        var agent = await AgentOnFeatureBranchAsync();

        await ReportAsync(status);

        Assert.NotNull(Stored(agent.Worktree).Evidence);
        Assert.Equal(40, Stored(agent.Worktree).Evidence!.Head.Length);
    }

    [Fact]
    public async Task A_modified_file_makes_the_worktree_dirty()
    {
        var agent = await AgentOnFeatureBranchAsync();
        await File.WriteAllTextAsync(Path.Combine(_repo, "new.txt"), "x");

        await ReportAsync("working");

        Assert.True(Stored(agent.Worktree).Evidence!.Dirty);
    }

    [Fact]
    public async Task Files_fleet_writes_under_its_own_folder_do_not_make_the_worktree_dirty()
    {
        var agent = await AgentOnFeatureBranchAsync();
        Directory.CreateDirectory(Path.Combine(_repo, ".fleet"));
        await File.WriteAllTextAsync(Path.Combine(_repo, ".fleet", "notices.md"), "x");

        await ReportAsync("working");

        Assert.False(Stored(agent.Worktree).Evidence!.Dirty);
    }

    [Fact]
    public async Task A_base_that_does_not_resolve_leaves_the_merge_base_empty_and_still_records_the_rest()
    {
        var agent = await AgentOnFeatureBranchAsync();
        _store.Save("techweb", agent with { BaseRef = "origin/gone" });

        var result = await ReportAsync("done");

        Assert.True(result.Succeeded, result.Error);
        var evidence = Stored(agent.Worktree).Evidence!;
        Assert.Equal("origin/gone", evidence.BaseRef);
        Assert.Equal(string.Empty, evidence.MergeBase);
        Assert.Equal(40, evidence.Head.Length);
    }

    [Fact]
    public async Task A_worktree_that_is_not_a_git_repository_is_still_reported_with_what_is_known()
    {
        var folder = Path.Combine(_root, "plain");
        Directory.CreateDirectory(folder);
        _store.Save("techweb", new AgentRecord(folder, "api", "feat/x", AgentHarness.Claude, "main", false));

        var result = await ReportAsync("done", verify: new VerifyReport("dotnet test", 0, "ok"));

        Assert.True(result.Succeeded, result.Error);
        var evidence = Stored(folder).Evidence!;
        Assert.Equal(string.Empty, evidence.Head);
        Assert.Equal(folder, evidence.Worktree);
        Assert.Equal(Verification.Ok, evidence.Verification);
    }

    [Fact]
    public async Task Done_with_the_verify_fields_stores_them_and_trims_the_output()
    {
        var agent = await AgentOnFeatureBranchAsync();
        var output = string.Join('\n', Enumerable.Range(1, 100).Select(i => $"line {i}"));

        await ReportAsync("done", verify: new VerifyReport("dotnet test", 0, output));

        var evidence = Stored(agent.Worktree).Evidence!;
        Assert.Equal("dotnet test", evidence.VerifyCommand);
        Assert.Equal(0, evidence.VerifyExitCode);
        Assert.StartsWith("line 61", evidence.VerifyOutput);
        Assert.EndsWith("line 100", evidence.VerifyOutput);
        Assert.Equal(40, evidence.VerifyOutput.Split('\n').Length);
    }

    [Fact]
    public async Task Failed_with_the_verify_fields_stores_them_too()
    {
        var agent = await AgentOnFeatureBranchAsync();

        await ReportAsync("failed", verify: new VerifyReport("dotnet build", 1, "error CS1002"));

        var evidence = Stored(agent.Worktree).Evidence!;
        Assert.Equal(1, evidence.VerifyExitCode);
        Assert.Equal("error CS1002", evidence.VerifyOutput);
    }

    [Fact]
    public async Task A_working_report_ignores_verify_fields_and_clears_an_earlier_verify()
    {
        var agent = await AgentOnFeatureBranchAsync();
        await ReportAsync("done", verify: new VerifyReport("dotnet test", 0, "ok"));

        await ReportAsync("working", verify: new VerifyReport("dotnet test", 0, "ok"));

        var evidence = Stored(agent.Worktree).Evidence!;
        Assert.Equal(string.Empty, evidence.VerifyCommand);
        Assert.Null(evidence.VerifyExitCode);
        Assert.Equal(string.Empty, evidence.VerifyOutput);
    }

    [Fact]
    public async Task Done_without_verify_fields_is_shown_unverified_and_is_not_rejected()
    {
        await AgentOnFeatureBranchAsync();

        var result = await ReportAsync("done", "all set");

        Assert.True(result.Succeeded, result.Error);
        Assert.StartsWith("api/feat/x: done (unverified) — all set [HEAD ", result.Value);
        Assert.EndsWith(", verify unverified]", result.Value);
    }

    [Fact]
    public async Task Done_with_a_non_zero_exit_code_is_shown_verify_failed_and_is_not_rejected()
    {
        await AgentOnFeatureBranchAsync();

        var result = await ReportAsync("done", "all set", new VerifyReport("dotnet test", 1, "1 failed"));

        Assert.True(result.Succeeded, result.Error);
        Assert.Contains("api/feat/x: done (verify failed) — all set [HEAD ", result.Value);
        Assert.EndsWith(", verify failed]", result.Value);
    }

    [Fact]
    public async Task Done_in_a_dirty_worktree_is_shown_dirty_and_is_not_rejected()
    {
        await AgentOnFeatureBranchAsync();
        await File.WriteAllTextAsync(Path.Combine(_repo, "new.txt"), "x");

        var result = await ReportAsync("done", "all set", new VerifyReport("dotnet test", 0, "ok"));

        Assert.True(result.Succeeded, result.Error);
        Assert.Contains("api/feat/x: done (dirty) — all set [HEAD ", result.Value);
        Assert.EndsWith(", verify ok]", result.Value);
    }

    [Fact]
    public async Task A_verified_clean_done_carries_no_flag()
    {
        await AgentOnFeatureBranchAsync();

        var result = await ReportAsync("done", "all set", new VerifyReport("dotnet test", 0, "ok"));

        Assert.Contains("api/feat/x: done — all set [HEAD ", result.Value);
        Assert.DoesNotContain("(", result.Value![..result.Value!.IndexOf('[')]);
    }

    [Fact]
    public async Task A_command_without_an_exit_code_is_still_unverified()
    {
        await AgentOnFeatureBranchAsync();

        var result = await ReportAsync("done", "all set", new VerifyReport("dotnet test", null, string.Empty));

        Assert.Contains("done (unverified)", result.Value);
    }

    [Fact]
    public async Task A_failed_report_names_its_verify_result_without_a_flag()
    {
        await AgentOnFeatureBranchAsync();

        var result = await ReportAsync("failed", "build broke", new VerifyReport("dotnet build", 1, "error"));

        Assert.StartsWith("api/feat/x: failed — build broke [HEAD ", result.Value);
        Assert.EndsWith(", verify failed]", result.Value);
    }

    [Fact]
    public async Task A_sub_orchestrator_is_not_flagged_unverified_and_no_report_is_rejected()
    {
        var folder = Path.Combine(_root, "orchestrations", "upgrade");
        Directory.CreateDirectory(folder);
        await GitAsync(folder, "init", "-b", "main");
        _store.Save(
            "techweb",
            new AgentRecord(folder, string.Empty, "upgrade", AgentHarness.Orchestrator, "main", false, Hidden: true));

        var result = await Handler().HandleAsync("techweb", _root, "upgrade", "done", "shipped");

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal("upgrade: done — shipped", result.Value![..23]);
        Assert.DoesNotContain("unverified", result.Value);
        Assert.Equal("done", Stored(folder).ReportLabel);
    }

    [Fact]
    public async Task A_sub_orchestrators_failing_verify_is_still_flagged()
    {
        var folder = Path.Combine(_root, "orchestrations", "upgrade");
        Directory.CreateDirectory(folder);
        _store.Save(
            "techweb",
            new AgentRecord(folder, string.Empty, "upgrade", AgentHarness.Orchestrator, "main", false, Hidden: true));

        var result = await Handler().HandleAsync(
            "techweb", _root, "upgrade", "done", "shipped", verify: new VerifyReport("make", 2, "boom"));

        Assert.StartsWith("upgrade: done (verify failed) — shipped", result.Value);
    }

    [Fact]
    public async Task The_owner_notice_for_done_carries_the_short_head_and_the_verify_result()
    {
        var agent = await AgentOnFeatureBranchAsync(harness: AgentHarness.Opencode);
        var head = await GitAsync(_repo, "rev-parse", "--short=7", "HEAD");
        var typed = new List<OwnerNotice>();

        await Handler((notice, _) =>
        {
            typed.Add(notice);
            return Task.FromResult(true);
        }).HandleAsync(
            "techweb", _root, "agent:api/feat/x", "done", "all set", verify: new VerifyReport("dotnet test", 0, "ok"));

        var notice = Assert.Single(typed);
        Assert.Contains($"api/feat/x: done — all set [HEAD {head}, verify ok]", notice.Message);
        Assert.Equal(head, Stored(agent.Worktree).Evidence!.ShortHead);
    }

    [Fact]
    public async Task The_owner_notice_for_failed_carries_the_short_head_and_the_verify_result()
    {
        await AgentOnFeatureBranchAsync(harness: AgentHarness.Opencode);
        var head = await GitAsync(_repo, "rev-parse", "--short=7", "HEAD");
        var typed = new List<OwnerNotice>();

        await Handler((notice, _) =>
        {
            typed.Add(notice);
            return Task.FromResult(true);
        }).HandleAsync("techweb", _root, "agent:api/feat/x", "failed", "stuck");

        Assert.Contains($"api/feat/x: failed — stuck [HEAD {head}, verify unverified]", Assert.Single(typed).Message);
    }

    [Fact]
    public async Task The_owner_notice_for_an_unverified_done_says_so()
    {
        await AgentOnFeatureBranchAsync(harness: AgentHarness.Opencode);
        var typed = new List<OwnerNotice>();

        await Handler((notice, _) =>
        {
            typed.Add(notice);
            return Task.FromResult(true);
        }).HandleAsync("techweb", _root, "agent:api/feat/x", "done", "all set");

        Assert.Contains("api/feat/x: done (unverified) — all set [HEAD ", Assert.Single(typed).Message);
    }

    [Fact]
    public async Task A_handler_without_git_still_records_the_report_with_no_evidence()
    {
        var agent = await AgentOnFeatureBranchAsync();

        var result = await new ReportStatusHandler(_store)
            .HandleAsync("techweb", _root, "agent:api/feat/x", "done", "all set");

        Assert.True(result.Succeeded, result.Error);
        Assert.Null(Stored(agent.Worktree).Evidence);
        Assert.Equal("api/feat/x: done (unverified) — all set", result.Value);
    }
}
