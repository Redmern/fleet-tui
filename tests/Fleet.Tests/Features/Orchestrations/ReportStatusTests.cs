using Fleet.Features.Orchestrations.ReportStatus;
using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;
using Fleet.Shared.Constants;

namespace Fleet.Tests.Features.Orchestrations;

public sealed class ReportStatusTests
{
    private readonly RecordingStore _store = new();

    private ReportStatusHandler Handler => new(_store);

    private AgentRecord Seed(string slug, string status = OrchestrationStatus.Working)
    {
        var record = new AgentRecord(
            $"C:/repos/techweb/.fleet/orchestrations/{slug}",
            string.Empty,
            slug,
            AgentHarness.Orchestrator,
            "origin/main",
            false,
            Hidden: true,
            Open: true,
            Status: status);

        _store.Saved.Add(record);

        return record;
    }

    private AgentRecord SeedAgent(string repository, string branch)
    {
        var record = new AgentRecord(
            $"C:/repos/techweb/{repository}/{branch}",
            repository,
            branch,
            AgentHarness.Nvim,
            "origin/main",
            true,
            Owner: "some-sub");

        _store.Saved.Add(record);

        return record;
    }

    [Fact]
    public void An_agent_reports_its_own_status_by_its_repository_and_branch()
    {
        SeedAgent("backend", "feature/login");

        var result = Handler.Handle(
            "techweb", "agent:backend/feature/login", OrchestrationStatus.Done, "endpoint added");

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(OrchestrationStatus.Done, Assert.Single(_store.Written).Status);
    }

    [Fact]
    public void An_agent_report_does_not_match_an_orchestrator()
    {
        Seed("backend");

        var result = Handler.Handle("techweb", "agent:backend/main", OrchestrationStatus.Done, "");

        Assert.False(result.Succeeded);
    }

    [Fact]
    public void The_main_orchestrator_cannot_report()
    {
        var result = Handler.Handle("techweb", caller: "  ", "done", "all set");

        Assert.False(result.Succeeded);
        Assert.Empty(_store.Written);
    }

    [Fact]
    public void An_unknown_caller_fails_without_writing()
    {
        var result = Handler.Handle("techweb", "ghost", "done", "all set");

        Assert.False(result.Succeeded);
        Assert.Empty(_store.Written);
    }

    [Fact]
    public void A_reported_status_updates_the_matching_orchestrator()
    {
        Seed("upgrade");

        var result = Handler.Handle("techweb", "upgrade", OrchestrationStatus.Done, "shipped");

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(OrchestrationStatus.Done, Assert.Single(_store.Written).Status);
    }

    [Fact]
    public void An_unknown_status_is_normalized_to_working()
    {
        Seed("upgrade");

        Handler.Handle("techweb", "upgrade", "banana", "hmm");

        Assert.Equal(OrchestrationStatus.Working, Assert.Single(_store.Written).Status);
    }

    [Fact]
    public void The_note_names_the_slug_the_status_and_the_summary()
    {
        Seed("upgrade");

        var result = Handler.Handle("techweb", "upgrade", OrchestrationStatus.Failed, "build broke");

        Assert.Contains("upgrade", result.Value);
        Assert.Contains(OrchestrationStatus.Failed, result.Value);
        Assert.Contains("build broke", result.Value);
    }

    [Fact]
    public void The_caller_match_ignores_case()
    {
        Seed("upgrade");

        var result = Handler.Handle("techweb", "UPGRADE", OrchestrationStatus.Done, "");

        Assert.True(result.Succeeded, result.Error);
    }

    private sealed class RecordingStore : IAgentStore
    {
        public List<AgentRecord> Saved { get; } = [];

        public List<AgentRecord> Written { get; } = [];

        public void Save(string project, AgentRecord agent) => Written.Add(agent);

        public IReadOnlyList<AgentRecord> List(string project) => Saved;

        public void Remove(string project, string worktree) { }
    }
}
