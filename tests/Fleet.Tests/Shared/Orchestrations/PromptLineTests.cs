using Fleet.Shared.Orchestrations;

namespace Fleet.Tests.Shared.Orchestrations;

public sealed class PromptLineTests
{
    [Theory]
    [InlineData("one line", "one line")]
    [InlineData("a\nb", "a b")]
    [InlineData("a\r\nb", "a b")]
    [InlineData("a\rb", "a b")]
    [InlineData("  a  \n\n   b  \n", "a b")]
    [InlineData("a  b\nc", "a  b c")]
    [InlineData(" \n \r\n ", "")]
    public void Newlines_collapse_to_a_single_space(string prompt, string line) =>
        Assert.Equal(line, PromptLine.OneLine(prompt));
}
