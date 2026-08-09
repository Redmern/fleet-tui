using Fleet.Features.Agents.NewAgent;

namespace Fleet.Tests.Features.Agents;

public class AgentBranchTests
{
    [Fact]
    public void A_name_and_a_base_cuts_that_name_from_that_base()
    {
        var plan = AgentBranch.Plan("feature/login", "develop");

        Assert.True(plan.Succeeded, plan.Error);
        Assert.Equal("feature/login", plan.Value!.Branch);
        Assert.Equal("develop", plan.Value.Base);
    }

    [Fact]
    public void A_name_with_no_base_leaves_the_base_for_the_default_branch_to_fill()
    {
        var plan = AgentBranch.Plan("feature/login", "");

        Assert.True(plan.Succeeded, plan.Error);
        Assert.Equal("feature/login", plan.Value!.Branch);
        Assert.Empty(plan.Value.Base);
    }

    [Fact]
    public void A_base_with_no_name_works_on_the_base_itself()
    {
        var plan = AgentBranch.Plan("", "develop");

        Assert.True(plan.Succeeded, plan.Error);
        Assert.Equal("develop", plan.Value!.Branch);
        Assert.Equal("develop", plan.Value.Base);
    }

    [Fact]
    public void A_remote_base_with_no_name_works_on_a_local_branch_of_the_same_name()
    {
        var plan = AgentBranch.Plan("", "origin/develop");

        Assert.True(plan.Succeeded, plan.Error);
        Assert.Equal("develop", plan.Value!.Branch);
        Assert.Equal("origin/develop", plan.Value.Base);
    }

    [Fact]
    public void Neither_given_is_refused_because_there_is_nothing_to_work_on()
    {
        var plan = AgentBranch.Plan("   ", "  ");

        Assert.False(plan.Succeeded);
        Assert.Equal(AgentBranch.NothingGiven, plan.Error);
    }

    [Theory]
    [InlineData("origin/develop", "develop")]
    [InlineData("origin/feature/login", "feature/login")]
    [InlineData("develop", "develop")]
    public void A_remote_reference_reduces_to_its_branch_name(string reference, string expected)
    {
        Assert.Equal(expected, AgentBranch.ShortNameOf(reference));
    }
}
