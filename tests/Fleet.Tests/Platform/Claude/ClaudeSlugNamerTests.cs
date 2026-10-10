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

    [Fact]
    public void A_wrapper_banner_before_the_slug_is_skipped()
    {
        var stdout = "mise ~/.config/mise/config.toml tools: claude@2.1.289\nadd-open-port\n";

        Assert.Equal("add-open-port", ClaudeSlugNamer.PickSlug(stdout));
    }

    [Fact]
    public void A_lone_slug_is_taken()
    {
        Assert.Equal("fix-login-bug", ClaudeSlugNamer.PickSlug("fix-login-bug"));
    }

    [Fact]
    public void Only_a_banner_yields_no_slug()
    {
        Assert.Null(ClaudeSlugNamer.PickSlug("mise ~/.config/mise/config.toml tools: claude@2.1.289\n"));
    }

    [Fact]
    public void Trailing_blank_lines_and_crlf_are_ignored()
    {
        Assert.Equal("add-open-port", ClaudeSlugNamer.PickSlug("add-open-port\r\n\r\n   \n"));
    }

    [Fact]
    public void Chatter_after_the_slug_is_not_taken_as_the_slug()
    {
        Assert.Equal("add-open-port", ClaudeSlugNamer.PickSlug("add-open-port\nHope that helps!\n"));
    }

    [Fact]
    public void Empty_output_yields_no_slug()
    {
        Assert.Null(ClaudeSlugNamer.PickSlug(string.Empty));
    }

    [Theory]
    [InlineData("Add-Open-Port")]
    [InlineData("add open port")]
    [InlineData("\"add-open-port\"")]
    [InlineData("add-open-port.")]
    [InlineData("add--open-port")]
    [InlineData("-add-open-port")]
    [InlineData("refactor")]
    [InlineData("one-two-three-four-five-six-seven")]
    [InlineData("tools:claude@2.1.289")]
    public void Lines_that_are_not_kebab_case_slugs_are_rejected(string line)
    {
        Assert.Null(ClaudeSlugNamer.PickSlug(line));
    }

    [Fact]
    public void An_overlong_slug_is_rejected()
    {
        Assert.Null(ClaudeSlugNamer.PickSlug(new string('a', 40) + "-" + new string('b', 40)));
    }
}
