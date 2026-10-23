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

    [Fact]
    public void Fleet_help_documents_the_todo_board_chord() =>
        Assert.Contains("n notifications, t todo board", HelpCommand.Text, StringComparison.Ordinal);

    [Fact]
    public void Fleet_help_documents_one_todo_board_with_an_all_projects_switch()
    {
        Assert.DoesNotContain("T todo board of all projects", HelpCommand.Text, StringComparison.Ordinal);
        Assert.Contains("a in the board switches to all projects", HelpCommand.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Fleet_help_documents_the_todo_sigil() =>
        Assert.Contains("a main-pane prompt starting with + (todoTrigger) does the same", HelpCommand.Text, StringComparison.Ordinal);

    [Fact]
    public void Fleet_help_documents_the_todo_verb() =>
        Assert.Contains("fleet todo [--project <p>] \"<idea>\"", HelpCommand.Text, StringComparison.Ordinal);

    [Theory]
    [InlineData("c new tab")]
    [InlineData("x/& close pane/tab")]
    [InlineData("s switch project, space menu")]
    public void Fleet_help_drops_the_moved_prefix_keys(string text) =>
        Assert.DoesNotContain(text, HelpCommand.Text, StringComparison.Ordinal);

    [Theory]
    [InlineData("fleet uninstall ")]
    [InlineData("--yes")]
    [InlineData("-y")]
    public void Fleet_help_documents_uninstall_and_its_flags(string text) =>
        Assert.Contains(text, HelpCommand.Text, StringComparison.Ordinal);
}
