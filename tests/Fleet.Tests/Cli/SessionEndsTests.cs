using Fleet.Cli.Commands;
using Fleet.Cli.Composition;
using Fleet.Tests.Features.Agents;
using Fleet.Shared.Status;
using Fleet.Features.Orchestrations.ReportStatus.Models;
using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;
using Fleet.Ports.Projects;
using Fleet.Ports.Projects.Models;
using Fleet.Shared.Constants;
using Fleet.Shared.Hooks;

namespace Fleet.Tests.Cli;

public sealed class SessionEndsTests
{
    private const string Root = "C:/repos/techweb";

    private const string Worktree = "C:/repos/techweb/backend/fix";

    private readonly Store _store = new();

    private readonly List<(string Root, OwnerNotice Notice)> _typed = [];

    private SessionEnds Ends(IReadOnlyList<Project>? projects = null) =>
        new(
            _store,
            new Projects(projects ?? [new Project("techweb", Root)]),
            (project, notice, _) =>
            {
                _typed.Add((project.Root, notice));
                return Task.FromResult(true);
            },
            () => new DateTimeOffset(2026, 11, 16, 9, 30, 0, TimeSpan.Zero),
            tickets: null);

    private static HookEvent End(string reason = "other", string worktree = Worktree) =>
        new("SessionEnd", worktree, "s1", Reason: reason);

    [Fact]
    public async Task A_session_that_ends_while_its_agent_is_working_fails_it_and_tells_the_owner()
    {
        _store.Add("techweb", Working());

        var handled = await Ends().HandleAsync(End());

        Assert.True(handled);
        var stored = Assert.Single(_store.Records);
        Assert.Equal(OrchestrationStatus.Failed, stored.Status);
        Assert.Equal("session ended without a report", stored.Summary);
        var (root, notice) = Assert.Single(_typed);
        Assert.Equal(Root, root);
        Assert.Contains("backend/fix: failed — session ended without a report", notice.Message);
    }

    [Fact]
    public async Task A_notice_that_cannot_be_typed_in_time_does_not_hold_the_hook_and_the_agent_stays_failed()
    {
        _store.Add("techweb", Working());
        var stuck = new SessionEnds(
            _store,
            new Projects([new Project("techweb", Root)]),
            async (_, _, ct) =>
            {
                await Task.Delay(Timeout.Infinite, ct);
                return true;
            },
            () => DateTimeOffset.UtcNow,
            tickets: null,
            deadline: TimeSpan.FromMilliseconds(1));

        var handled = await stuck.HandleAsync(End());

        Assert.True(handled);
        Assert.Equal(OrchestrationStatus.Failed, Assert.Single(_store.Records).Status);
    }

    [Fact]
    public async Task Nothing_is_typed_and_so_no_mux_is_built_when_there_is_no_working_record_to_fail()
    {
        _store.Add("techweb", Working() with { Status = OrchestrationStatus.Done });

        await Ends().HandleAsync(End());
        await Ends().HandleAsync(End(worktree: "C:/elsewhere"));
        await Ends().HandleAsync(End("clear"));

        Assert.Empty(_typed);
    }

    [Fact]
    public async Task A_project_whose_name_changes_when_sanitized_is_still_found_by_its_session_key()
    {
        _store.Add("my project", Working());

        var handled = await Ends([new Project("my project", Root)]).HandleAsync(End());

        Assert.True(handled);
        Assert.Equal(OrchestrationStatus.Failed, Assert.Single(_store.Records).Status);
    }

    [Theory]
    [InlineData("clear")]
    [InlineData("resume")]
    public async Task A_session_that_carries_on_changes_nothing(string reason)
    {
        _store.Add("techweb", Working());

        var handled = await Ends().HandleAsync(End(reason));

        Assert.False(handled);
        Assert.NotEqual(OrchestrationStatus.Failed, Assert.Single(_store.Records).Status);
        Assert.Empty(_typed);
    }

    [Fact]
    public async Task A_worktree_fleet_does_not_manage_is_ignored()
    {
        _store.Add("techweb", Working());

        Assert.False(await Ends().HandleAsync(End(worktree: "C:/elsewhere")));
        Assert.Empty(_typed);
    }

    [Fact]
    public async Task A_record_of_a_project_fleet_no_longer_knows_is_left_alone()
    {
        _store.Add("gone", Working());

        Assert.False(await Ends().HandleAsync(End()));
        Assert.NotEqual(OrchestrationStatus.Failed, Assert.Single(_store.Records).Status);
    }

    [Fact]
    public async Task Another_hook_event_is_not_a_session_end()
    {
        _store.Add("techweb", Working());

        Assert.False(await Ends().HandleAsync(new HookEvent("Stop", Worktree, "s1")));
    }

    [Fact]
    public async Task The_fleet_hook_command_fails_a_working_agent_whose_session_ends_for_good()
    {
        _store.Add("techweb", Working());
        var states = new RecordingHookState();

        await HookCommand.HandleAsync(End(), states, Ends(), DateTime.UtcNow);

        Assert.Equal(OrchestrationStatus.Failed, Assert.Single(_store.Records).Status);
        Assert.Single(_typed);
    }

    [Fact]
    public async Task The_fleet_hook_command_keeps_recording_status_for_an_ordinary_event()
    {
        _store.Add("techweb", Working());
        var states = new RecordingHookState();

        await HookCommand.HandleAsync(new HookEvent("PreToolUse", Worktree, "s1"), states, Ends(), DateTime.UtcNow);

        Assert.Single(states.Reported);
        Assert.Empty(_typed);
    }

    [Fact]
    public async Task Events_of_an_agent_fleet_is_closing_are_ignored_until_its_next_session_starts()
    {
        var folder = Path.Combine(Path.GetTempPath(), "fleet-tests", Path.GetRandomFileName());
        var inner = Path.Combine(folder, "src");
        Directory.CreateDirectory(inner);

        try
        {
            ClosingMark.Write(folder);
            _store.Add("techweb", Working() with { Worktree = folder });
            var states = new RecordingHookState();

            await HookCommand.HandleAsync(new HookEvent("PostToolUse", inner, "s1"), states, Ends(), DateTime.UtcNow);
            await HookCommand.HandleAsync(new HookEvent("SessionEnd", inner, "s1", Reason: "other"), states, Ends(), DateTime.UtcNow);

            Assert.Empty(states.Reported);
            Assert.NotEqual(OrchestrationStatus.Failed, Assert.Single(_store.Records).Status);
            Assert.True(ClosingMark.Exists(folder));

            await HookCommand.HandleAsync(new HookEvent("SessionStart", inner, "s2"), states, Ends(), DateTime.UtcNow);

            Assert.False(ClosingMark.Exists(folder));
            Assert.Single(states.Reported);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static AgentRecord Working() =>
        new(Worktree, "backend", "fix", AgentHarness.Claude, "origin/main", true, Open: true);

    private sealed class Projects(IReadOnlyList<Project> all) : IProjectStore
    {
        public Project? Load(string name) => all.FirstOrDefault(p => p.Name == name);

        public IReadOnlyList<Project> List() => all;

        public void Save(Project project)
        {
        }

        public void Remove(string name)
        {
        }
    }

    private sealed class Store : IAgentStore
    {
        private string _project = string.Empty;

        public List<AgentRecord> Records { get; } = [];

        public void Add(string project, AgentRecord record)
        {
            _project = project;
            Records.Add(record);
        }

        public AgentLocation? FindByWorktree(string worktree) =>
            Records.FirstOrDefault(a => a.Worktree == worktree) is { } found ? new AgentLocation(Fleet.Shared.ProjectName.Sanitize(_project), found) : null;

        public void Save(string project, AgentRecord agent) => Records.Add(agent);

        public bool Replace(string project, AgentRecord agent) => true;

        public bool Update(string project, string worktree, Func<AgentRecord, AgentRecord> change)
        {
            var index = Records.FindIndex(a => a.Worktree == worktree);

            if (index < 0 || project != _project)
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
