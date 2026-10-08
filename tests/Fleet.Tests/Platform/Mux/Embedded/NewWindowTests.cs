using Fleet.Platform.Mux.Embedded.Host;

namespace Fleet.Tests.Platform.Mux.Embedded;

public sealed class NewWindowTests
{
    private static readonly string[] Attach = ["attach", "--project", "techweb"];

    private static WindowLaunch? Plan(
        bool windows, Dictionary<string, string>? vars = null, string[]? onPath = null, Dictionary<string, string>? env = null) =>
        NewWindow.Plan(
            "fleet",
            Attach,
            env ?? [],
            name => (vars ?? []).GetValueOrDefault(name),
            program => (onPath ?? []).Contains(program),
            windows);

    [Fact]
    public void Windows_terminal_gets_a_new_window_of_its_own()
    {
        var plan = Plan(true, new() { ["WT_SESSION"] = "abc" }, ["wt"])!;

        Assert.Equal("wt", plan.Program);
        Assert.Equal(["-w", "new", "fleet", "attach", "--project", "techweb"], plan.Args);
    }

    [Fact]
    public void Windows_terminal_opens_the_same_profile_the_window_came_from()
    {
        var plan = Plan(true, new() { ["WT_SESSION"] = "abc", ["WT_PROFILE_ID"] = "{771d8af3-bc33-41e8-a761-e0875d661369}" }, ["wt"])!;

        Assert.Equal(["-w", "new", "-p", "{771d8af3-bc33-41e8-a761-e0875d661369}", "fleet", "attach", "--project", "techweb"], plan.Args);
    }

    [Fact]
    public void A_plain_console_on_windows_opens_a_console_window_through_the_shell()
    {
        var plan = Plan(true)!;

        Assert.Equal("fleet", plan.Program);
        Assert.Equal(Attach, plan.Args);
        Assert.True(plan.ShellExecute);
    }

    [Fact]
    public void Endpoint_and_config_overrides_travel_into_the_new_window()
    {
        var plan = Plan(true, new() { ["WT_SESSION"] = "abc" }, ["wt"], new() { ["FLEET_ENDPOINT"] = "fleet-test" })!;

        Assert.Equal(["-w", "new", "cmd.exe", "/d", "/c"], plan.Args.Take(5));
        Assert.StartsWith("set \"FLEET_ENDPOINT=fleet-test\"&& ", plan.Args[5], StringComparison.Ordinal);
        Assert.EndsWith("attach --project techweb", plan.Args[5], StringComparison.Ordinal);
    }

    [Fact]
    public void Unix_uses_the_terminal_named_by_TERMINAL_else_the_debian_alternative()
    {
        Assert.Equal(
            ["-e", "fleet", "attach", "--project", "techweb"],
            Plan(false, new() { ["TERMINAL"] = "foot" }, ["foot"])!.Args);
        Assert.Equal("x-terminal-emulator", Plan(false, onPath: ["x-terminal-emulator"])!.Program);
        Assert.Equal(
            ["-e", "env", "FLEET_ENDPOINT=/tmp/f.sock", "fleet", "attach", "--project", "techweb"],
            Plan(false, onPath: ["x-terminal-emulator"], env: new() { ["FLEET_ENDPOINT"] = "/tmp/f.sock" })!.Args);
    }

    [Fact]
    public void Without_any_terminal_to_start_there_is_no_plan()
    {
        Assert.Null(Plan(false));
    }
}
