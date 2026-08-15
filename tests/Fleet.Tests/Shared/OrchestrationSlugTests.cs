using Fleet.Shared.Constants;
using Fleet.Shared.Orchestrations;

namespace Fleet.Tests.Shared;

public class OrchestrationSlugTests
{
    [Theory]
    [InlineData("Add a create story endpoint", "add-a-create-story-endpoint")]
    [InlineData("  UPGRADE   the Node runtime!! ", "upgrade-the-node-runtime")]
    [InlineData("one two three four five six seven eight", "one-two-three-four-five-six")]
    [InlineData("!!!", "task")]
    [InlineData("", "task")]
    public void A_prompt_becomes_a_slug(string prompt, string expected)
    {
        Assert.Equal(expected, OrchestrationSlug.Of(prompt));
    }

    [Fact]
    public void The_slug_never_runs_past_forty_characters()
    {
        var slug = OrchestrationSlug.Of(new string('a', 100));

        Assert.True(slug.Length <= 40);
    }

    [Fact]
    public void A_taken_slug_gets_a_numeric_suffix()
    {
        var taken = new HashSet<string> { "task", "task-2" };

        Assert.Equal("task-3", OrchestrationSlug.Unique("task", taken.Contains));
    }

    [Fact]
    public void A_free_slug_is_used_as_is()
    {
        Assert.Equal("fresh", OrchestrationSlug.Unique("fresh", _ => false));
    }

    [Fact]
    public void An_orchestrator_opens_interactive_claude_fresh_and_resumes_on_reopen()
    {
        Assert.Equal(
            [AgentHarness.Claude],
            AgentHarness.CommandFor(AgentHarness.Orchestrator, fresh: true));

        Assert.Equal(
            [AgentHarness.Claude, AgentHarness.ResumeArgument],
            AgentHarness.CommandFor(AgentHarness.Orchestrator));

        Assert.True(AgentHarness.IsOrchestrator(AgentHarness.Orchestrator));
    }

    [Fact]
    public void The_orchestrator_harness_survives_normalisation_instead_of_degrading_to_nvim()
    {
        Assert.Equal(AgentHarness.Orchestrator, AgentHarness.Normalize(AgentHarness.Orchestrator));
        Assert.Equal(AgentHarness.Nvim, AgentHarness.Normalize("emacs"));
    }
}
