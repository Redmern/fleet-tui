using Fleet.Platform.Claude;

namespace Fleet.Tests.Platform.Claude;

public class ClaudeSlugNamerTests
{
    [Fact]
    public void The_prompt_asks_for_a_short_kebab_case_slug_and_carries_the_task_verbatim()
    {
        var built = ClaudeSlugNamer.BuildPrompt("Add a \"create story\" endpoint\nin backend");

        Assert.Contains("kebab-case", built);
        Assert.Contains("Add a \"create story\" endpoint\nin backend", built);
    }
}
