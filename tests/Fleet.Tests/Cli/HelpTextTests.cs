using Fleet.Cli.Commands;

namespace Fleet.Tests.Cli;

public class HelpTextTests
{
    [Theory]
    [InlineData("a agents")]
    [InlineData("s session")]
    [InlineData("t tabs")]
    [InlineData("g configure")]
    [InlineData("g c ")]
    [InlineData("m maintenance")]
    [InlineData("Q quit fleet")]
    [InlineData("r reload keys")]
    [InlineData("o open port")]
    [InlineData("x close tab")]
    public void Fleet_help_documents_the_ctrl_s_groups(string text) =>
        Assert.Contains(text, HelpCommand.Text, StringComparison.Ordinal);

    [Theory]
    [InlineData("c new tab")]
    [InlineData("x/& close pane/tab")]
    [InlineData("s switch project, space menu")]
    public void Fleet_help_drops_the_moved_prefix_keys(string text) =>
        Assert.DoesNotContain(text, HelpCommand.Text, StringComparison.Ordinal);
}
