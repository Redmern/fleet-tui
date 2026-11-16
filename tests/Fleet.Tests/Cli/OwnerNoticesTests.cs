using System.Globalization;
using Fleet.Cli.Composition;
using Fleet.Features.Orchestrations.ReportStatus.Models;
using Fleet.Platform.Mux.Fake;
using Fleet.Ports;
using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;
using Fleet.Shared.Constants;
using Fleet.Shared.Messaging;

namespace Fleet.Tests.Cli;

public sealed class OwnerNoticesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fleet-tests", Path.GetRandomFileName());

    private readonly FakeMuxDriver _mux = new();

    private readonly Store _store = new();

    private readonly Log _log = new();

    public OwnerNoticesTests() => Directory.CreateDirectory(_root);

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

    private OwnerNotices Notices => new("techweb", _root, _mux, _store, _log, () => false);

    private AgentRecord Sub(string slug) =>
        new(Path.Combine(_root, ".fleet", "orchestrations", slug), string.Empty, slug, AgentHarness.Orchestrator,
            string.Empty, false, InNvim: false);

    [Fact]
    public async Task A_notice_is_appended_to_the_notices_file_and_the_read_prompt_is_typed()
    {
        var sub = Sub("lead");
        var pane = await _mux.SpawnAsync(new() { Cwd = sub.Worktree, NewWindow = true });

        var told = await Notices.TypeAsync(new OwnerNotice(sub, "techweb-sub-lead", sub.Worktree, "[fleet] api/x: done"));

        Assert.True(told);
        var line = Assert.Single(File.ReadAllLines(NoticesOf(sub)));
        Assert.EndsWith(" [fleet] api/x: done", line, StringComparison.Ordinal);
        Assert.True(DateTimeOffset.TryParse(
            line[..line.IndexOf(' ', StringComparison.Ordinal)], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _));
        Assert.Equal([AgentHarness.NoticesPrompt, "\r"], _mux.SentTo(pane));
    }

    [Fact]
    public async Task Two_notices_in_a_row_are_both_kept_and_the_instruction_file_is_not_touched()
    {
        var sub = Sub("lead");
        var pane = await _mux.SpawnAsync(new() { Cwd = sub.Worktree, NewWindow = true });
        var instruction = Path.Combine(sub.Worktree, ".fleet", AgentHarness.AgentInstructionFile);
        Directory.CreateDirectory(Path.GetDirectoryName(instruction)!);
        File.WriteAllText(instruction, "do the real work");

        await Notices.TypeAsync(new OwnerNotice(sub, "techweb-sub-lead", sub.Worktree, "[fleet] api/x: done"));
        await Notices.TypeAsync(new OwnerNotice(sub, "techweb-sub-lead", sub.Worktree, "[fleet] web/y: failed"));

        var lines = File.ReadAllLines(NoticesOf(sub));
        Assert.Equal(2, lines.Length);
        Assert.EndsWith(" [fleet] api/x: done", lines[0], StringComparison.Ordinal);
        Assert.EndsWith(" [fleet] web/y: failed", lines[1], StringComparison.Ordinal);
        Assert.Equal("do the real work", File.ReadAllText(instruction));
        Assert.Equal(
            [AgentHarness.NoticesPrompt, "\r", AgentHarness.NoticesPrompt, "\r"], _mux.SentTo(pane));
    }

    [Fact]
    public async Task A_notice_with_line_breaks_stays_one_line_in_the_notices_file()
    {
        var sub = Sub("lead");
        await _mux.SpawnAsync(new() { Cwd = sub.Worktree, NewWindow = true });

        await Notices.TypeAsync(new OwnerNotice(sub, "techweb-sub-lead", sub.Worktree, HarnessRoute.OwnerNotice("api/x:\ndone")));

        Assert.Single(File.ReadAllLines(NoticesOf(sub)));
    }

    private static string NoticesOf(AgentRecord owner) =>
        Path.Combine(owner.Worktree, ".fleet", AgentHarness.NoticesFile);

    [Fact]
    public async Task A_notice_that_cannot_be_appended_is_not_told_and_the_failure_is_logged_not_thrown()
    {
        var sub = Sub("lead");
        var pane = await _mux.SpawnAsync(new() { Cwd = sub.Worktree, NewWindow = true });
        Directory.CreateDirectory(Path.GetDirectoryName(NoticesOf(sub))!);
        using var holder = new FileStream(NoticesOf(sub), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var notices = new OwnerNotices("techweb", _root, _mux, _store, _log, () => false, new ImmediateClock());

        var told = await notices.TypeAsync(new OwnerNotice(sub, "techweb-sub-lead", sub.Worktree, "[fleet] api/x: done"));

        Assert.False(told);
        Assert.Contains(
            _log.Lines, l => l.Contains("could not write the notice for techweb-sub-lead", StringComparison.Ordinal));
        Assert.Empty(_mux.SentTo(pane));
    }

    [Fact]
    public async Task An_owner_without_a_pane_is_not_told_and_the_miss_is_logged()
    {
        var sub = Sub("lead");

        var told = await Notices.TypeAsync(new OwnerNotice(sub, "techweb-sub-lead", sub.Worktree, "[fleet] api/x: done"));

        Assert.False(told);
        Assert.Contains(_log.Lines, l => l.Contains("could not reach techweb-sub-lead", StringComparison.Ordinal));
    }

    private sealed class ImmediateClock : TimeProvider
    {
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            base.CreateTimer(callback, state, dueTime == Timeout.InfiniteTimeSpan ? dueTime : TimeSpan.Zero, period);
    }

    private sealed class Store : IAgentStore
    {
        public List<AgentRecord> Agents { get; } = [];

        public void Save(string project, AgentRecord agent) => Agents.Add(agent);

        public IReadOnlyList<AgentRecord> List(string project) => Agents;

        public bool Replace(string project, AgentRecord agent) => true;

        public bool Update(string project, string worktree, Func<AgentRecord, AgentRecord> change) =>
            List(project).FirstOrDefault(a => a.Worktree == worktree) is { } current && Replace(project, change(current));

        public bool Remove(string project, string worktree) => true;
    }

    private sealed class Log : IFleetLog
    {
        public List<string> Lines { get; } = [];

        public void Swallowed(Exception e) => Lines.Add(e.Message);

        public void Write(string line) => Lines.Add(line);

        public IReadOnlyList<string> Tail(int lines) => Lines;
    }
}
