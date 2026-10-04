using System.Reflection;
using Fleet.Cli.Commands;
using Fleet.Shared.Keymap.Enums;

namespace Fleet.Tests.Cli;

public class MenuBackTests
{
    private static string RepoRoot { get; } =
        typeof(MenuBackTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .First(a => a.Key == "RepoRoot")
            .Value!;

    private static string Source(params string[] path) =>
        File.ReadAllText(Path.Combine([RepoRoot, "src", "Fleet", .. path]));

    [Theory]
    [InlineData(FleetAction.EditSettings)]
    [InlineData(FleetAction.EditKeybinds)]
    [InlineData(FleetAction.ViewLogs)]
    [InlineData(FleetAction.EditAidlcMode)]
    [InlineData(FleetAction.EditAutoClose)]
    [InlineData(FleetAction.EditClaudeProfile)]
    public void A_settings_screen_goes_back_to_the_settings_menu(FleetAction action) =>
        Assert.Equal(FleetAction.OpenSettings, MenuCommand.Parent(action));

    [Theory]
    [InlineData(FleetAction.SwitchProject)]
    [InlineData(FleetAction.ListAgents)]
    [InlineData(FleetAction.Notifications)]
    [InlineData(FleetAction.Remotes)]
    [InlineData(FleetAction.QuitFleet)]
    [InlineData(FleetAction.OpenSettings)]
    public void A_top_level_screen_goes_back_to_the_fleet_menu(FleetAction action) =>
        Assert.Equal(FleetAction.None, MenuCommand.Parent(action));

    public static TheoryData<string[]> Screens => new()
    {
        new[] { "Ui", "FleetPicker.cs" },
        new[] { "Ui", "FleetTabbedPicker.cs" },
        new[] { "Ui", "FleetDialog.cs" },
        new[] { "Ui", "FleetPrompt.cs" },
        new[] { "Features", "Menu", "ShowMenu", "ShowMenuView.cs" },
        new[] { "Features", "Menu", "EditSettings", "EditSettingsView.cs" },
        new[] { "Features", "Menu", "EditAidlc", "EditAidlcView.cs" },
        new[] { "Features", "Menu", "EditKeybinds", "EditKeybindsView.cs" },
        new[] { "Features", "Diagnostics", "ViewLogs", "ViewLogsView.cs" },
        new[] { "Features", "Remotes", "ManageRemotes", "ManageRemotesView.cs" },
        new[] { "Features", "Notifications", "ShowNotices", "ShowNoticesView.cs" },
        new[] { "Features", "Dashboard", "ShowDashboard", "ShowDashboardView.cs" },
        new[] { "Features", "Repositories", "AddRepository", "AddRepositoryView.cs" },
        new[] { "Features", "Repositories", "Secrets", "SecretsView.cs" },
        new[] { "Features", "Agents", "NewAgent", "NewAgentView.cs" },
        new[] { "Features", "Agents", "ListAgents", "ListAgentsView.cs" },
    };

    [Theory]
    [MemberData(nameof(Screens))]
    public void Every_screen_reachable_from_the_menu_goes_back_on_backspace(string[] path)
    {
        var source = Source(path);

        Assert.Contains("FleetKeys.GoesBack(key", source);
        Assert.Contains("FleetModal.Back();", source);
    }

    // A text field must keep backspace for deleting what was typed; it only
    // goes back once the field is empty.
    [Theory]
    [InlineData("FleetDialog.cs")]
    [InlineData("FleetPrompt.cs")]
    public void A_text_field_only_goes_back_when_it_is_empty(string file)
    {
        var source = Source("Ui", file);

        Assert.Contains("field.KeyDown", source);
        Assert.Contains("FleetKeys.GoesBack(key, field.Text)", source);
    }

    // Backspace is a key you may want to bind, so the capture dialog takes it
    // as the new key rather than going back.
    [Fact]
    public void The_key_capture_dialog_records_backspace_instead_of_going_back() =>
        Assert.DoesNotContain("GoesBack", Source("Ui", "FleetKeyCapture.cs"));
}
