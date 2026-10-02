using Fleet.Features.Agents.TrackStatus;
using Fleet.Platform.Hooks;
using Fleet.Ports.Agents;
using Fleet.Shared.Hooks;
using Fleet.Shared.Status.Enums;
using Fleet.Shared.Status.Models;

namespace Fleet.Tests.Features.Agents;

public sealed class TrackStatusTests
{
    private sealed class FakeStateStore : IAgentStateStore
    {
        public List<AgentReport> Reported { get; } = [];

        public Task ReportAsync(AgentReport report, CancellationToken ct = default)
        {
            Reported.Add(report);
            return Task.CompletedTask;
        }

        public Task<AgentSnapshot> GetSnapshotAsync(CancellationToken ct = default) =>
            Task.FromResult(new AgentSnapshot(Reported));
    }

    private static readonly DateTime Now = new(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task A_contract_event_is_stored()
    {
        var store = new FakeStateStore();

        var stored = await new TrackStatusHandler(store).HandleAsync(new HookEvent("PreToolUse", "C:/w/a", "s1"), Now);

        Assert.True(stored);
        Assert.Equal(AgentState.Working, Assert.Single(store.Reported).State);
    }

    [Fact]
    public async Task A_subagent_stop_is_not_stored()
    {
        var store = new FakeStateStore();

        var stored = await new TrackStatusHandler(store).HandleAsync(new HookEvent("Stop", "C:/w/a", "s1", AgentId: "sub"), Now);

        Assert.False(stored);
        Assert.Empty(store.Reported);
    }

    [Fact]
    public void The_payload_claude_sends_becomes_a_hook_event()
    {
        var payload = HookIo.Read(new StringReader("""
            {"session_id":"abc","transcript_path":"C:/t/abc.jsonl","cwd":"C:/w/a",
             "hook_event_name":"Notification","notification_type":"permission_prompt",
             "message":"Claude needs your permission","agent_id":"sub-1"}
            """));

        var hook = HookIo.Event(payload);

        Assert.Equal(new HookEvent("Notification", "C:/w/a", "abc", "C:/t/abc.jsonl", "sub-1", "permission_prompt"), hook);
    }

    [Fact]
    public void The_folder_claude_was_started_in_wins_over_where_the_session_has_cd_ed_to()
    {
        var payload = HookIo.Read(new StringReader("""{"cwd":"C:/w/a/src","hook_event_name":"PreToolUse"}"""));

        Assert.Equal("C:/w/a", HookIo.Event(payload, "C:/w/a").Cwd);
        Assert.Equal("C:/w/a/src", HookIo.Event(payload, string.Empty).Cwd);
        Assert.Equal("C:/w/a/src", HookIo.Event(payload).Cwd);
    }

    [Fact]
    public void The_session_start_source_is_read_from_the_payload()
    {
        var payload = HookIo.Read(new StringReader("""{"cwd":"C:/w/a","hook_event_name":"SessionStart","source":"compact"}"""));

        Assert.Equal("compact", HookIo.Event(payload).Source);
    }

    [Fact]
    public void An_empty_payload_is_an_event_with_nothing_in_it()
    {
        var hook = HookIo.Event(HookIo.Read(new StringReader(string.Empty)));

        Assert.Null(HookStatus.ReportFor(hook, Now));
    }
}
