using Fleet.Shared.Orchestrations;
using Fleet.Shared.Settings.Models;

namespace Fleet.Tests.Shared;

public class ModelRoutingTests
{
    private static readonly ModelRoutingRequest Request =
        new("Add a \"create story\" endpoint\nin backend", "feature", Research: true, Repository: "backend");

    [Fact]
    public void The_prompt_carries_the_task_verbatim_with_profile_research_and_repository()
    {
        var prompt = ModelRouting.BuildPrompt(Request);

        Assert.Contains("Add a \"create story\" endpoint\nin backend", prompt);
        Assert.Contains("feature", prompt);
        Assert.Contains("research: yes", prompt);
        Assert.Contains("backend", prompt);
    }

    [Fact]
    public void The_prompt_names_every_allowed_model_and_effort_and_the_reply_format()
    {
        var prompt = ModelRouting.BuildPrompt(Request with { Profile = null, Research = false, Repository = null });

        foreach (var value in ModelRouting.Models.Concat(ModelRouting.Efforts))
        {
            Assert.Contains(value, prompt);
        }

        Assert.Contains("model=<model> effort=<effort>", prompt);
        Assert.Contains("research: no", prompt);
    }

    [Theory]
    [InlineData("model=sonnet effort=high", "sonnet", "high")]
    [InlineData("  model=opus   effort=xhigh  \r\n", "opus", "xhigh")]
    [InlineData("MODEL=Haiku Effort=LOW", "haiku", "low")]
    [InlineData("model=fable effort=max", "fable", "max")]
    [InlineData("mise tools: claude@2.1.289\nmodel=opus effort=medium\n\n", "opus", "medium")]
    public void A_valid_reply_yields_the_model_and_effort(string stdout, string model, string effort)
    {
        Assert.Equal(new RoleModel(model, effort), ModelRouting.Parse(stdout));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n  ")]
    [InlineData("model=gpt-4 effort=high")]
    [InlineData("model=sonnet effort=extreme")]
    [InlineData("model=inherit effort=medium")]
    [InlineData("model=auto effort=medium")]
    [InlineData("model=claude-opus-5-5 effort=high")]
    [InlineData("model=sonnet")]
    [InlineData("effort=high model=sonnet")]
    [InlineData("I would pick model=sonnet effort=high")]
    [InlineData("model=sonnet effort=high because it is a small task")]
    [InlineData("`model=sonnet effort=high`")]
    public void An_invalid_reply_yields_nothing(string stdout)
    {
        Assert.Null(ModelRouting.Parse(stdout));
    }

    [Fact]
    public void Chatter_after_the_answer_does_not_hide_it()
    {
        Assert.Equal(new RoleModel("sonnet", "low"), ModelRouting.Parse("model=sonnet effort=low\nHope that helps!\n"));
    }
}
