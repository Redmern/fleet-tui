using Fleet.Features.Orchestrations.ListSubs;
using Fleet.Ports.Agents.Models;
using Fleet.Shared.Constants;

namespace Fleet.Tests.Features.Orchestrations;

public sealed class SubStatusTests
{
    private static AgentRecord Sub(string status) =>
        new("C:/repos/techweb/.fleet/orchestrations/upgrade", string.Empty, "upgrade", AgentHarness.Orchestrator,
            "origin/main", false, Status: status);

    private static AgentRecord Child(string status, bool hidden = false) =>
        new($"C:/repos/techweb/backend/{Guid.NewGuid():N}", "backend", "story", AgentHarness.Claude, "origin/main",
            true, Hidden: hidden, Owner: "upgrade", Status: status);

    [Theory]
    [InlineData("")]
    [InlineData("working")]
    [InlineData("waiting")]
    [InlineData("idle")]
    public void A_sub_that_has_not_reported_done_or_failed_keeps_its_live_status(string status)
    {
        Assert.Null(SubStatus.Derive(Sub(status), [Child("failed"), Child("")]));
    }

    [Theory]
    [InlineData("done")]
    [InlineData("failed")]
    public void A_finished_sub_without_children_shows_its_own_report(string status)
    {
        Assert.Equal(status, SubStatus.Derive(Sub(status), []));
    }

    [Theory]
    [InlineData("")]
    [InlineData("working")]
    [InlineData("idle")]
    [InlineData("waiting")]
    public void A_finished_sub_with_an_unfinished_child_is_idle(string child)
    {
        Assert.Equal(SubStatus.Idle, SubStatus.Derive(Sub("done"), [Child("done"), Child(child)]));
        Assert.Equal(SubStatus.Idle, SubStatus.Derive(Sub("failed"), [Child("failed"), Child(child)]));
    }

    [Fact]
    public void A_done_sub_whose_children_all_finished_and_one_failed_is_failed()
    {
        Assert.Equal(OrchestrationStatus.Failed, SubStatus.Derive(Sub("done"), [Child("done"), Child("failed")]));
    }

    [Theory]
    [InlineData("done")]
    [InlineData("failed")]
    public void A_sub_whose_children_all_finished_without_failure_shows_its_own_report(string status)
    {
        Assert.Equal(status, SubStatus.Derive(Sub(status), [Child("done"), Child(" Done ")]));
    }

    [Fact]
    public void Hidden_children_are_ignored()
    {
        Assert.Equal(
            OrchestrationStatus.Done,
            SubStatus.Derive(Sub("done"), [Child("done"), Child("working", hidden: true), Child("failed", hidden: true)]));
    }
}
