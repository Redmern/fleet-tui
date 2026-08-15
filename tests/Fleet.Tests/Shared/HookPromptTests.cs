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
