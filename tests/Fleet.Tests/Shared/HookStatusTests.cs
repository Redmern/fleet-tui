using Fleet.Shared.Hooks;
using Fleet.Shared.Status.Enums;

namespace Fleet.Tests.Shared;

public sealed class HookStatusTests
{
    private static readonly DateTime Now = new(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc);

    private static HookEvent Event(string name, string agentId = "", string notification = "") =>
        new(name, "C:/w/a", "session-1", "C:/t/session-1.jsonl", agentId, notification);

    [Theory]
    [InlineData("UserPromptSubmit", AgentState.Working)]
    [InlineData("PreToolUse", AgentState.Working)]
    [InlineData("PostToolUse", AgentState.Working)]
    [InlineData("Stop", AgentState.Idle)]
    [InlineData("SessionStart", AgentState.Idle)]
    [InlineData("PermissionRequest", AgentState.Blocked)]
    [InlineData("Notification", AgentState.Blocked)]
    [InlineData("SessionEnd", AgentState.Unknown)]
    public void Each_event_reports_the_state_the_contract_fixes(string name, AgentState state)
    {
        Assert.Equal(state, HookStatus.ReportFor(Event(name), Now)!.State);
    }

    [Fact]
    public void Stalled_is_never_reported()
    {
        Assert.DoesNotContain(
            HookStatus.Events,
            name => HookStatus.ReportFor(Event(name), Now)?.State == AgentState.Stalled);
    }

    [Fact]
    public void A_report_keys_on_the_hook_cwd_and_carries_session_and_transcript()
    {
        var report = HookStatus.ReportFor(Event("PreToolUse"), Now)!;

        Assert.Equal("C:/w/a", report.Worktree);
        Assert.Equal("session-1", report.Session);
        Assert.Equal("C:/t/session-1.jsonl", report.Transcript);
        Assert.Equal(Now, report.At);
    }

    [Fact]
    public void Only_a_session_start_marks_a_new_session()
    {
        Assert.True(HookStatus.ReportFor(Event("SessionStart"), Now)!.StartsSession);
        Assert.False(HookStatus.ReportFor(Event("Stop"), Now)!.StartsSession);
        Assert.False(HookStatus.ReportFor(Event("PreToolUse"), Now)!.StartsSession);
    }

    [Fact]
    public void An_approved_tool_finishing_clears_the_block()
    {
        Assert.Equal(AgentState.Working, HookStatus.ReportFor(Event("PostToolUse"), Now)!.State);
        Assert.Equal(string.Empty, HookStatus.ReportFor(Event("PostToolUse"), Now)!.Reason);
    }

    [Fact]
    public void An_event_without_a_cwd_reports_nothing()
    {
        Assert.Null(HookStatus.ReportFor(new HookEvent("PreToolUse", " "), Now));
    }

    [Theory]
    [InlineData("SubagentStop")]
    [InlineData("SubagentStart")]
    [InlineData("PostToolUseFailure")]
    [InlineData("")]
    public void Events_outside_the_contract_report_nothing(string name)
    {
        Assert.Null(HookStatus.ReportFor(Event(name), Now));
    }

    [Theory]
    [InlineData("Stop")]
    [InlineData("SessionStart")]
    [InlineData("SessionEnd")]
    public void A_subagent_event_never_marks_the_parent_done(string name)
    {
        Assert.Null(HookStatus.ReportFor(Event(name, agentId: "agent-7"), Now));
    }

    [Fact]
    public void A_subagent_idle_notification_never_marks_the_parent_done()
    {
        Assert.Null(HookStatus.ReportFor(Event("Notification", "agent-7", "idle_prompt"), Now));
    }

    [Theory]
    [InlineData("PreToolUse", AgentState.Working)]
    [InlineData("PermissionRequest", AgentState.Blocked)]
    public void A_subagent_still_reports_work_and_blocks(string name, AgentState state)
    {
        Assert.Equal(state, HookStatus.ReportFor(Event(name, agentId: "agent-7"), Now)!.State);
    }

    [Theory]
    [InlineData("PermissionRequest", "", HookStatus.PermissionReason)]
    [InlineData("Notification", "permission_prompt", HookStatus.PermissionReason)]
    [InlineData("Notification", "elicitation_dialog", HookStatus.InputReason)]
    [InlineData("Notification", "", HookStatus.InputReason)]
    public void A_block_says_whether_it_waits_on_a_permission_or_on_input(string name, string type, string reason)
    {
        Assert.Equal(reason, HookStatus.ReportFor(Event(name, notification: type), Now)!.Reason);
    }

    [Fact]
    public void The_idle_reminder_is_idle_not_blocked()
    {
        Assert.Equal(AgentState.Idle, HookStatus.ReportFor(Event("Notification", notification: "idle_prompt"), Now)!.State);
    }

    [Fact]
    public void An_auth_notification_reports_nothing()
    {
        Assert.Null(HookStatus.ReportFor(Event("Notification", notification: "auth_success"), Now));
    }
}
