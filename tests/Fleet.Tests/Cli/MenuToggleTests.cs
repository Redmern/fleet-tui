using System.Reflection;
using Fleet.Cli.Commands;
using Fleet.Features.Menu.ShowMenu;
using Fleet.Shared.Keymap.Enums;

namespace Fleet.Tests.Cli;

public class MenuToggleTests
{
    private static string RepoRoot { get; } =
        typeof(MenuToggleTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .First(a => a.Key == "RepoRoot")
            .Value!;

    private static string MenuSource() =>
        File.ReadAllText(Path.Combine(RepoRoot, "src", "Fleet", "Cli", "Commands", "MenuCommand.cs"));

    [Theory]
    [InlineData(FleetAction.EditShowMenuKeys)]
    [InlineData(FleetAction.EditButtonHints)]
    [InlineData(FleetAction.EditMainOrchestratorInNvim)]
    [InlineData(FleetAction.EditSubOrchestratorsInNvim)]
    [InlineData(FleetAction.EditNvimConfig)]
    public void A_toggle_action_runs_headless(FleetAction action)
    {
        Assert.True(FleetMenus.IsToggle(action));
        Assert.True(MenuCommand.RunsHeadless(action));
    }

    [Theory]
    [InlineData(FleetAction.None)]
    [InlineData(FleetAction.OpenSettings)]
    [InlineData(FleetAction.EditTheme)]
    [InlineData(FleetAction.EditAutoClose)]
    [InlineData(FleetAction.QuitFleet)]
    public void Anything_else_still_opens_the_menu_ui(FleetAction action) =>
        Assert.False(MenuCommand.RunsHeadless(action));

    [Fact]
    public void Toggles_are_handled_before_the_ui_starts_so_no_pane_is_drawn()
    {
        var source = MenuSource();
        var run = source.IndexOf("public static async Task<int> RunAsync", StringComparison.Ordinal);
        var headless = source.IndexOf("RunsHeadless(requested)", run, StringComparison.Ordinal);
        var ui = source.IndexOf("IApplication app = FleetUi.Start()", run, StringComparison.Ordinal);

        Assert.True(headless > run, "RunAsync should branch on RunsHeadless(requested)");
        Assert.True(headless < ui, "the toggle branch must run before FleetUi.Start draws anything");
    }

    [Fact]
    public void The_interactive_menu_and_the_headless_path_share_one_toggle()
    {
        var source = MenuSource();

        Assert.DoesNotContain("FleetMenus.Flip(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveShowMenuKeys", source, StringComparison.Ordinal);
    }
}
