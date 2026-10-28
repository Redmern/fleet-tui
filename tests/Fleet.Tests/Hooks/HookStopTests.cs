using System.Text.Json;
using Fleet.Platform.Hooks;

namespace Fleet.Tests.Hooks;

public sealed class HookStopTests
{
    [Fact]
    public void Stopping_a_prompt_exits_zero_with_continue_false_and_the_note_as_the_stop_reason()
    {
        var output = new StringWriter();

        var exit = HookIo.Stop("fleet: dispatched sub-orchestrator tidy-logs", output);

        Assert.Equal(0, exit);
        using var stop = JsonDocument.Parse(output.ToString());
        Assert.False(stop.RootElement.GetProperty("continue").GetBoolean());
        Assert.Equal("fleet: dispatched sub-orchestrator tidy-logs", stop.RootElement.GetProperty("stopReason").GetString());
    }

    [Fact]
    public void Stopping_a_prompt_never_uses_the_block_decision_because_claude_code_restores_a_blocked_prompt()
    {
        var output = new StringWriter();

        HookIo.Stop("fleet: could not dispatch — no mux", output);

        using var stop = JsonDocument.Parse(output.ToString());
        Assert.False(stop.RootElement.TryGetProperty("decision", out _));
        Assert.False(stop.RootElement.TryGetProperty("reason", out _));
    }
}
