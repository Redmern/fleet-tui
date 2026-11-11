using Fleet.Shared.Constants;
using Fleet.Shared.Messaging;

namespace Fleet.Tests.Shared.Messaging;

public sealed class HarnessRouteTests
{
    [Theory]
    [InlineData(AgentHarness.Claude, AgentHarness.Claude, true)]
    [InlineData(AgentHarness.Claude, AgentHarness.Opencode, false)]
    [InlineData(AgentHarness.Opencode, AgentHarness.Claude, false)]
    [InlineData(AgentHarness.Opencode, AgentHarness.Opencode, false)]
    public void Only_two_native_messaging_harnesses_talk_natively(string caller, string target, bool native) =>
        Assert.Equal(native, HarnessRoute.Native(caller, target));

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void A_caller_of_an_unknown_harness_does_not_talk_natively(string caller) =>
        Assert.False(HarnessRoute.Native(caller, AgentHarness.Claude));

    [Fact]
    public void An_editor_pane_leaves_the_route_to_its_inbox_as_before() =>
        Assert.True(HarnessRoute.Native(AgentHarness.Claude, AgentHarness.Nvim));

    [Theory]
    [InlineData(AgentHarness.Claude, AgentHarness.Claude)]
    [InlineData(AgentHarness.Opencode, AgentHarness.Opencode)]
    [InlineData(AgentHarness.Nvim, AgentHarness.Nvim)]
    [InlineData(AgentHarness.Orchestrator, AgentHarness.Claude)]
    [InlineData("bogus", AgentHarness.Nvim)]
    public void A_record_runs_the_harness_it_stores(string harness, string runs) =>
        Assert.Equal(runs, HarnessRoute.Of(harness));

    [Fact]
    public void A_sub_orchestrator_runs_the_orchestrator_harness_it_is_given() =>
        Assert.Equal(
            AgentHarness.Opencode,
            HarnessRoute.Of(AgentHarness.Orchestrator, orchestrator: AgentHarness.Opencode));

    [Fact]
    public void An_owner_notice_is_one_line_and_names_the_tools_to_follow_up_with()
    {
        var notice = HarnessRoute.OwnerNotice("api/x: done — added\r\nthe endpoint");

        Assert.DoesNotContain('\n', notice);
        Assert.Contains("api/x: done — added the endpoint", notice);
        Assert.Contains("agent_status", notice);
    }
}
