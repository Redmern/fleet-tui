using Fleet.Features.Orchestrations.ReportStatus;
using Fleet.Shared.Hooks;

namespace Fleet.Tests.Shared;

public class HookPromptTests
{
    [Fact]
    public void A_prompt_starting_with_the_trigger_is_taken_and_the_trigger_stripped()
    {
        var decision = HookPrompt.Intercepted(", add oauth login", ",");

        Assert.True(decision.Take);
        Assert.Equal("add oauth login", decision.Task);
    }

    [Theory]
    [InlineData("[Cross-session idle notice] \"techweb-backend-fix\", which you asked to be notified about, is idle now")]
    [InlineData("[Cross-session delivery notice] your message to \"techweb-main\" was held")]
    public void A_cross_session_notice_never_dispatches_even_with_a_bracket_trigger(string prompt)
    {
        Assert.False(HookPrompt.Intercepted(prompt, "[").Take);
        Assert.False(HookPrompt.Intercepted(prompt, ",").Take);
    }

    [Theory]
    [InlineData("<cross-session-message from=\"uds:\\\\.\\pipe\\LOCAL\\cc-msg-1\" from-name=\"head\" from-mode=\"prompting\">\n, task\n</cross-session-message>")]
    [InlineData("  <cross-session-message from=\"uds:x\">\ntask\n</cross-session-message>")]
    [InlineData("<task-notification>\n<task-id>b1</task-id>\n</task-notification>")]
    public void A_message_from_another_session_or_a_task_notice_never_dispatches_even_with_a_lt_trigger(string prompt)
    {
        Assert.False(HookPrompt.Intercepted(prompt, "<").Take);
        Assert.False(HookPrompt.Intercepted(prompt, ",").Take);
    }

    [Theory]
    [InlineData(",", "merged")]
    [InlineData(",", ", dispatch this: add tests")]
    [InlineData("<", "build broke")]
    public void An_agents_report_summary_sent_to_its_orchestrator_never_dispatches(string trigger, string summary)
    {
        var delivered =
            "<cross-session-message from=\"uds:\\\\.\\pipe\\LOCAL\\cc-msg-2\" from-name=\"techweb-backend-fix\" "
            + $"from-mode=\"prompting\">\n{ReportNote.For("backend/fix", "done", summary)}\n</cross-session-message>";

        Assert.False(HookPrompt.Intercepted(delivered, trigger).Take);
    }

    [Fact]
    public void The_space_after_the_trigger_is_optional()
    {
        Assert.Equal("task", HookPrompt.Intercepted(",task", ",").Task);
    }

    [Fact]
    public void Leading_whitespace_before_the_trigger_is_tolerated()
    {
        Assert.True(HookPrompt.Intercepted("   , task", ",").Take);
    }

    [Fact]
    public void A_lone_trigger_with_no_task_passes_through()
    {
        Assert.False(HookPrompt.Intercepted(",", ",").Take);
        Assert.False(HookPrompt.Intercepted(",   ", ",").Take);
    }

    [Fact]
    public void A_prompt_without_the_trigger_passes_through()
    {
        Assert.False(HookPrompt.Intercepted("hello there", ",").Take);
        Assert.False(HookPrompt.Intercepted("a, b", ",").Take);
    }

    [Fact]
    public void A_different_trigger_is_honoured()
    {
        Assert.True(HookPrompt.Intercepted("; do it", ";").Take);
        Assert.False(HookPrompt.Intercepted(", do it", ";").Take);
    }

    [Fact]
    public void An_empty_trigger_never_intercepts()
    {
        Assert.False(HookPrompt.Intercepted(", anything", "").Take);
    }
}
