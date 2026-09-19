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
    public void The_titled_verb_keeps_the_title_and_the_command_after_the_separator_intact()
    {
        var invocation = CommandLine.Parse(
            ["titled", "--title", "sub files", "--", "nvim", "-c", "lua print('a b')"]);

        Assert.Equal(FleetVerb.Titled, invocation.Verb);
        Assert.Equal("sub files", invocation.Title);
        Assert.Equal(["nvim", "-c", "lua print('a b')"], invocation.Tail);
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

    [Fact]
    public void Dispatch_and_hook_dispatch_are_verbs()
    {
        Assert.Equal(FleetVerb.Dispatch, CommandLine.Parse(["dispatch"]).Verb);
        Assert.Equal(FleetVerb.HookDispatch, CommandLine.Parse(["hook-dispatch"]).Verb);
    }

    [Fact]
    public void A_free_text_prompt_is_read_whether_it_comes_before_or_after_the_flags()
    {
        Assert.Equal(
            "add oauth login",
            CommandLine.Parse(["dispatch", "--project", "techweb", "add oauth login"]).Text);

        Assert.Equal(
            "add oauth login",
            CommandLine.Parse(["dispatch", "add oauth login", "--project", "techweb"]).Text);
    }

    [Fact]
    public void A_flag_value_is_never_taken_for_the_free_text()
    {
        Assert.Null(CommandLine.Parse(["dispatch", "--project", "techweb"]).Text);
    }

    [Fact]
    public void A_double_dash_passes_the_rest_through_as_the_prompt()
    {
        Assert.Equal(
            "--weird prompt text",
            CommandLine.Parse(["dispatch", "--project", "p", "--", "--weird", "prompt", "text"]).Text);
    }

    [Fact]
    public void The_caller_flag_is_parsed()
    {
        Assert.Equal(
            "sub-slug",
            CommandLine.Parse(["dispatch", "--project", "p", "--caller", "sub-slug", "task"]).Caller);
    }

    [Fact]
    public void The_version_flag_is_parsed_for_update()
    {
        var invocation = CommandLine.Parse(["update", "--version", "0.3.0"]);

        Assert.Equal(FleetVerb.Update, invocation.Verb);
        Assert.Equal("0.3.0", invocation.Version);
    }

    [Fact]
    public void Update_without_a_version_flag_leaves_it_null()
    {
        Assert.Null(CommandLine.Parse(["update"]).Version);
    }
}
