using Fleet.Features.Orchestrations.ReportStatus;
using Fleet.Features.Orchestrations.ReportStatus.Models;
using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;
using Fleet.Ports.Todos;
using Fleet.Shared.Constants;

namespace Fleet.Tests.Features.Orchestrations;

public sealed class SessionEndedTests
{
    private const string Root = "C:/repos/techweb";

    private static readonly DateTimeOffset Now = new(2026, 11, 16, 9, 30, 0, TimeSpan.Zero);

    private readonly MemoryStore _store = new();

    private readonly List<OwnerNotice> _typed = [];

    private ReportStatusHandler Handler(ITicketStatusSink? tickets = null) =>
        new(_store, () => Now, tickets: tickets, typeToOwner: (notice, _) =>
        {
            _typed.Add(notice);
            return Task.FromResult(true);
        });

    private AgentRecord Worker(string status = OrchestrationStatus.Working, string owner = "", bool open = true)
    {
        var record = new AgentRecord(
            "C:/repos/techweb/backend/fix", "backend", "fix", AgentHarness.Claude, "origin/main", true,
            Owner: owner, Open: open, Status: status, InstructedAt: "2026-11-16T09:00:00.0000000+00:00");
        _store.Records.Add(record);

        return record;
    }

    private AgentRecord Sub(string slug = "upgrade", string status = OrchestrationStatus.Working, string harness = "")
    {
        var record = new AgentRecord(
            $"{Root}/.fleet/orchestrations/{slug}", string.Empty, slug, AgentHarness.Orchestrator, "origin/main", false,
            Hidden: true, Open: true, Status: status, OrchestratorHarness: harness);
        _store.Records.Add(record);

        return record;
    }

    [Fact]
    public async Task A_working_agent_whose_session_ended_is_failed_and_its_owner_told()
    {
        var worker = Worker();

        var ended = await Handler().SessionEndedAsync("techweb", Root, worker);

        Assert.True(ended);
        var stored = Assert.Single(_store.Records);
        Assert.Equal(OrchestrationStatus.Failed, stored.Status);
        Assert.Equal("session ended without a report", stored.Summary);
        Assert.Equal(Now.ToString("O"), stored.ReportedAt);
        Assert.Equal("2026-11-16T09:00:00.0000000+00:00", stored.InstructedAt);
        var notice = Assert.Single(_typed);
        Assert.Null(notice.Owner);
        Assert.Equal("techweb-main", notice.Name);
        Assert.Contains("backend/fix: failed — session ended without a report", notice.Message);
    }

    [Fact]
    public async Task The_owner_is_told_by_typing_even_when_it_could_be_reached_by_SendMessage()
    {
        _store.Records.Add(new AgentRecord(
            $"{Root}/.fleet/orchestrations/lead", string.Empty, "lead", AgentHarness.Orchestrator, "origin/main", false,
            Hidden: true, Open: true, OrchestratorHarness: AgentHarness.Claude, Owner: "main"));
        var worker = Worker(owner: "lead");

        await Handler().SessionEndedAsync("techweb", Root, worker);

        var notice = Assert.Single(_typed);
        Assert.Equal("lead", notice.Owner!.Branch);
    }

    [Theory]
    [InlineData(OrchestrationStatus.Done)]
    [InlineData(OrchestrationStatus.Failed)]
    public async Task An_agent_that_already_reported_a_result_is_left_alone(string status)
    {
        var worker = Worker(status);

        var ended = await Handler().SessionEndedAsync("techweb", Root, worker);

        Assert.False(ended);
        Assert.Equal(status, Assert.Single(_store.Records).Status);
        Assert.Empty(_typed);
    }

    [Fact]
    public async Task An_agent_that_was_stopped_is_left_alone()
    {
        var worker = Worker(open: false);

        var ended = await Handler().SessionEndedAsync("techweb", Root, worker);

        Assert.False(ended);
        Assert.Equal(OrchestrationStatus.Working, Assert.Single(_store.Records).Status);
        Assert.Empty(_typed);
    }

    [Fact]
    public async Task A_claude_top_level_sub_is_failed_without_a_notice()
    {
        var sub = Sub();
        var tickets = new RecordingTickets();

        await Handler(tickets).SessionEndedAsync("techweb", Root, sub);

        Assert.Equal(OrchestrationStatus.Failed, Assert.Single(_store.Records).Status);
        Assert.Empty(_typed);
        Assert.Equal(("upgrade", "failed", "session ended without a report"), Assert.Single(tickets.Reports));
    }

    [Fact]
    public async Task A_non_claude_top_level_sub_tells_the_main_orchestrator()
    {
        var sub = Sub(harness: AgentHarness.Opencode);

        await Handler().SessionEndedAsync("techweb", Root, sub);

        var notice = Assert.Single(_typed);
        Assert.Null(notice.Owner);
        Assert.Contains("upgrade: failed — session ended without a report", notice.Message);
    }

    [Fact]
    public async Task An_agent_fleet_is_closing_on_purpose_is_never_failed()
    {
        var folder = Path.Combine(Path.GetTempPath(), "fleet-tests", Path.GetRandomFileName());
        Directory.CreateDirectory(folder);

        try
        {
            var worker = Worker() with { Worktree = folder };
            _store.Records[0] = worker;
            Fleet.Shared.Status.ClosingMark.Write(folder);

            var ended = await Handler().SessionEndedAsync("techweb", Root, worker);

            Assert.False(ended);
            Assert.NotEqual(OrchestrationStatus.Failed, Assert.Single(_store.Records).Status);
            Assert.Empty(_typed);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task A_top_level_sub_with_no_project_root_files_no_ticket_report()
    {
        var sub = Sub();
        var tickets = new RecordingTickets();

        await Handler(tickets).SessionEndedAsync("techweb", string.Empty, sub);

        Assert.Empty(tickets.Reports);
    }

    private sealed class RecordingTickets : ITicketStatusSink
    {
        public List<(string Orchestration, string Status, string Summary)> Reports { get; } = [];

        public void Reported(string projectRoot, string orchestration, string status, string summary) =>
            Reports.Add((orchestration, status, summary));
    }

    private sealed class MemoryStore : IAgentStore
    {
        public List<AgentRecord> Records { get; } = [];

        public void Save(string project, AgentRecord agent) => Records.Add(agent);

        public bool Replace(string project, AgentRecord agent) => true;

        public bool Update(string project, string worktree, Func<AgentRecord, AgentRecord> change)
        {
            var index = Records.FindIndex(a => a.Worktree == worktree);

            if (index < 0)
            {
                return false;
            }

            Records[index] = change(Records[index]);
            return true;
        }

        public IReadOnlyList<AgentRecord> List(string project) => Records;

        public bool Remove(string project, string worktree) => true;
    }
}
