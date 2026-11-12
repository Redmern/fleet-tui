using Fleet.Cli.Composition;
using Fleet.Features.Orchestrations.ReportStatus.Models;
using Fleet.Platform.Mux.Fake;
using Fleet.Ports;
using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;
using Fleet.Shared.Constants;

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
    public async Task A_notice_is_written_as_is_into_the_owner_folder_and_the_read_prompt_is_typed()
    {
        var sub = Sub("lead");
        var pane = await _mux.SpawnAsync(new() { Cwd = sub.Worktree, NewWindow = true });

        var told = await Notices.TypeAsync(new OwnerNotice(sub, "techweb-sub-lead", sub.Worktree, "[fleet] api/x: done"));

        Assert.True(told);
        Assert.Equal(
            "[fleet] api/x: done",
            File.ReadAllText(Path.Combine(sub.Worktree, ".fleet", AgentHarness.AgentInstructionFile)));
        Assert.Equal([AgentHarness.AgentInstructionPrompt, "\r"], _mux.SentTo(pane));
    }

    [Fact]
    public async Task An_owner_without_a_pane_is_not_told_and_the_miss_is_logged()
    {
        var sub = Sub("lead");

        var told = await Notices.TypeAsync(new OwnerNotice(sub, "techweb-sub-lead", sub.Worktree, "[fleet] api/x: done"));

        Assert.False(told);
        Assert.Contains(_log.Lines, l => l.Contains("could not reach techweb-sub-lead", StringComparison.Ordinal));
    }

    private sealed class Store : IAgentStore
    {
        public List<AgentRecord> Agents { get; } = [];

        public void Save(string project, AgentRecord agent) => Agents.Add(agent);

        public IReadOnlyList<AgentRecord> List(string project) => Agents;

        public bool Replace(string project, AgentRecord agent) => true;

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
