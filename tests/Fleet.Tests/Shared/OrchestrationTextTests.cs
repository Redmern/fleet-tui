using Fleet.Shared.Orchestrations;
using Fleet.Shared.Orchestrations.Models;

namespace Fleet.Tests.Shared;

public class OrchestrationTextTests
{
    private static readonly OrchestrationBrief Brief =
        new("techweb", "add-endpoint", "Add an endpoint", "2026-09-22T00:00:00Z");

    [Fact]
    public void With_no_override_the_instructions_carry_the_built_in_how_you_work_section()
    {
        var text = OrchestrationText.Instructions(Brief);

        Assert.Contains(OrchestrationText.DefaultHowYouWork, text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_override_falls_back_to_the_built_in_section(string? howYouWork)
    {
        var text = OrchestrationText.Instructions(Brief, howYouWork);

        Assert.Contains(OrchestrationText.DefaultHowYouWork, text);
    }

    [Fact]
    public void A_project_override_replaces_the_how_you_work_section()
    {
        var text = OrchestrationText.Instructions(Brief, "  Only ever touch the api/ folder.  ");

        Assert.Contains("Only ever touch the api/ folder.", text);
        Assert.DoesNotContain(OrchestrationText.DefaultHowYouWork, text);
    }

    [Fact]
    public void The_header_and_reporting_sections_stay_fixed_regardless_of_the_override()
    {
        var text = OrchestrationText.Instructions(Brief, "Custom rules here.");

        Assert.Contains("# Sub-orchestrator: add-endpoint", text);
        Assert.Contains("Read TASK.md. It holds the request verbatim. Do not edit it.", text);
        Assert.Contains("call fleet_report with", text);
    }
}
