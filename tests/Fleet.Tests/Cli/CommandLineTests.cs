using Fleet.Cli;
using Fleet.Cli.Enums;

namespace Fleet.Tests.Cli;

public class CommandLineTests
{
    [Fact]
    public void No_arguments_means_pick_a_project()
    {
        Assert.Equal(FleetVerb.Pick, CommandLine.Parse([]).Verb);
    }

    [Theory]
    [InlineData("dash", FleetVerb.Dash)]
    [InlineData("menu", FleetVerb.Menu)]
    [InlineData("request", FleetVerb.Request)]
    [InlineData("apply-keybinds", FleetVerb.ApplyKeybinds)]
    [InlineData("doctor", FleetVerb.Doctor)]
    [InlineData("help", FleetVerb.Help)]
    [InlineData("--help", FleetVerb.Help)]
    [InlineData("-h", FleetVerb.Help)]
    public void Each_verb_is_recognised(string arg, FleetVerb expected)
    {
        Assert.Equal(expected, CommandLine.Parse([arg]).Verb);
    }

    [Fact]
    public void An_unknown_verb_keeps_its_text_so_the_error_can_name_it()
    {
        var invocation = CommandLine.Parse(["wibble"]);

        Assert.Equal(FleetVerb.Unknown, invocation.Verb);
        Assert.Equal("wibble", invocation.Raw);
    }

    [Fact]
    public void Flags_are_read_regardless_of_order()
    {
        var first = CommandLine.Parse(["request", "--project", "techweb", "--action", "refresh"]);
        var second = CommandLine.Parse(["request", "--action", "refresh", "--project", "techweb"]);

        Assert.Equal("techweb", first.Project);
        Assert.Equal("refresh", first.Action);
        Assert.Equal(first.Project, second.Project);
        Assert.Equal(first.Action, second.Action);
    }

    [Fact]
    public void A_missing_flag_is_null_rather_than_empty()
    {
        var invocation = CommandLine.Parse(["dash"]);

        Assert.Null(invocation.Project);
        Assert.Null(invocation.Action);
    }

    [Fact]
    public void A_flag_with_no_value_after_it_is_null_not_a_crash()
    {
        var invocation = CommandLine.Parse(["dash", "--project"]);

        Assert.Null(invocation.Project);
    }

    [Fact]
    public void A_blank_flag_value_counts_as_missing()
    {
        var invocation = CommandLine.Parse(["dash", "--project", "   "]);

        Assert.Null(invocation.Project);
    }

    [Fact]
    public void A_verb_is_never_mistaken_for_a_flag_value()
    {
        var invocation = CommandLine.Parse(["--project", "techweb"]);

        Assert.Equal(FleetVerb.Unknown, invocation.Verb);
        Assert.Null(invocation.Project);
    }
}
