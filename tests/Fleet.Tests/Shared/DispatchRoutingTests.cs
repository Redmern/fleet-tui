using Fleet.Shared.Aidlc.Enums;
using Fleet.Shared.Orchestrations;
using Fleet.Shared.Orchestrations.Enums;
using Fleet.Shared.Settings.Enums;

namespace Fleet.Tests.Shared;

public sealed class DispatchRoutingTests
{
    [Fact]
    public void Without_a_repository_a_dispatch_goes_to_a_sub_orchestrator()
    {
        var route = DispatchRouting.Decide("fix the login timeout", AidlcMode.Off, Profile.Express);

        Assert.Equal(DispatchTarget.SubOrchestrator, route.Target);
        Assert.False(route.Research);
    }

    [Fact]
    public void With_a_repository_and_aidlc_off_a_repo_agent_is_started_directly()
    {
        var route = DispatchRouting.Decide(
            "fix the login timeout", AidlcMode.Off, Profile.Express, repository: "backend");

        Assert.Equal(DispatchTarget.RepoAgent, route.Target);
        Assert.Equal("fix the login timeout", route.Prompt);
        Assert.Null(route.Aidlc);
    }

    [Fact]
    public void A_blank_repository_counts_as_none()
    {
        var route = DispatchRouting.Decide("fix it", AidlcMode.Off, Profile.Express, repository: "  ");

        Assert.Equal(DispatchTarget.SubOrchestrator, route.Target);
    }

    [Fact]
    public void Aidlc_on_keeps_the_sub_orchestrator_even_with_a_repository()
    {
        var route = DispatchRouting.Decide(
            "fix the login timeout", AidlcMode.On, Profile.Bugfix, repository: "backend");

        Assert.Equal(DispatchTarget.SubOrchestrator, route.Target);
        Assert.Equal((Profile.Bugfix, ProfileSource.ProjectDefault), route.Aidlc);
    }

    [Fact]
    public void Manual_mode_with_a_profile_prefix_keeps_the_sub_orchestrator()
    {
        var route = DispatchRouting.Decide(
            "feature: add oauth", AidlcMode.Manual, Profile.Express, repository: "backend");

        Assert.Equal(DispatchTarget.SubOrchestrator, route.Target);
        Assert.Equal("add oauth", route.Prompt);
        Assert.Equal((Profile.Feature, ProfileSource.Prefix), route.Aidlc);
    }

    [Fact]
    public void Manual_mode_without_a_prefix_starts_the_repo_agent_directly()
    {
        var route = DispatchRouting.Decide(
            "add oauth", AidlcMode.Manual, Profile.Express, repository: "backend");

        Assert.Equal(DispatchTarget.RepoAgent, route.Target);
        Assert.Null(route.Aidlc);
    }

    [Fact]
    public void Aidlc_off_leaves_a_profile_prefix_in_the_direct_task()
    {
        var route = DispatchRouting.Decide(
            "feature: add oauth", AidlcMode.Off, Profile.Express, repository: "backend");

        Assert.Equal(DispatchTarget.RepoAgent, route.Target);
        Assert.Equal("feature: add oauth", route.Prompt);
    }

    [Fact]
    public void Asking_for_research_keeps_the_sub_and_marks_it_research_even_with_a_repository()
    {
        var route = DispatchRouting.Decide(
            "compare the caching options", AidlcMode.Off, Profile.Express, repository: "backend", research: true);

        Assert.Equal(DispatchTarget.SubOrchestrator, route.Target);
        Assert.True(route.Research);
    }

    [Fact]
    public void A_research_prefix_in_manual_mode_keeps_a_research_sub_even_with_a_repository()
    {
        var route = DispatchRouting.Decide(
            "research: compare the caching options", AidlcMode.Manual, Profile.Express, repository: "backend");

        Assert.Equal(DispatchTarget.SubOrchestrator, route.Target);
        Assert.True(route.Research);
    }

    [Fact]
    public void The_research_profile_marks_the_sub_as_research()
    {
        var route = DispatchRouting.Decide(
            "compare the caching options", AidlcMode.On, Profile.Express, Profile.Research);

        Assert.True(route.Research);
        Assert.Equal((Profile.Research, ProfileSource.Argument), route.Aidlc);
    }

    [Fact]
    public void Another_profile_is_not_research()
    {
        var route = DispatchRouting.Decide("research: the caching options", AidlcMode.Off, Profile.Express);

        Assert.False(route.Research);
    }
}
