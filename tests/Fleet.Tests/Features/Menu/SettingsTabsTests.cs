using Fleet.Features.Menu.EditSettings;
using Fleet.Shared.Settings;
using Fleet.Shared.Settings.Enums;
using Fleet.Shared.Settings.Models;

namespace Fleet.Tests.Features.Menu;

public class SettingsTabsTests
{
    private static int RowOf(HarnessTool tool) => SettingsRows.Tools.ToList().IndexOf(tool) + 1;

    private static SettingsConfig Mixed() =>
        SettingsConfig.Default
            .With(HarnessTool.NewAgent, ActionPolicy.Ask).With(HarnessTool.NewAgent, AskChannel.Both)
            .With(HarnessTool.StopAgent, ActionPolicy.Ask).With(HarnessTool.StopAgent, AskChannel.FleetDialog)
            .With(HarnessTool.ListAgents, ActionPolicy.Ask).With(HarnessTool.ListAgents, AskChannel.ClaudePermission)
            .With(HarnessTool.TellAgent, ActionPolicy.Allow).With(HarnessTool.TellAgent, AskChannel.FleetDialog);

    [Fact]
    public void The_tabs_are_all_fleet_dialog_and_claude()
    {
        Assert.Equal(["All", "Fleet dialog", "Claude"], SettingsTabs.Titles);
    }

    [Theory]
    [InlineData(SettingsTabs.All, SettingsTabs.FleetDialog)]
    [InlineData(SettingsTabs.FleetDialog, SettingsTabs.Claude)]
    [InlineData(SettingsTabs.Claude, SettingsTabs.All)]
    public void Next_and_previous_wrap_around(int from, int to)
    {
        Assert.Equal(to, SettingsTabs.Next(from));
        Assert.Equal(from, SettingsTabs.Previous(to));
    }

    [Fact]
    public void The_all_tab_lists_every_row()
    {
        Assert.Equal(Enumerable.Range(0, SettingsRows.Count), SettingsTabs.Visible(SettingsTabs.All, Mixed()));
    }

    [Fact]
    public void The_fleet_dialog_tab_lists_ask_rows_the_fleet_dialog_asks_for()
    {
        var visible = SettingsTabs.Visible(SettingsTabs.FleetDialog, Mixed());

        Assert.Contains(RowOf(HarnessTool.NewAgent), visible);
        Assert.Contains(RowOf(HarnessTool.StopAgent), visible);
        Assert.DoesNotContain(RowOf(HarnessTool.ListAgents), visible);
        Assert.DoesNotContain(RowOf(HarnessTool.TellAgent), visible);
        Assert.All(visible, i => Assert.NotEqual(HarnessTool.None, SettingsRows.ToolAt(i)));
        Assert.All(visible, i => Assert.Equal(ActionPolicy.Ask, Mixed().RuleFor(SettingsRows.ToolAt(i)).Policy));
    }

    [Fact]
    public void The_claude_tab_lists_ask_rows_claude_asks_for()
    {
        var visible = SettingsTabs.Visible(SettingsTabs.Claude, Mixed());

        Assert.Contains(RowOf(HarnessTool.NewAgent), visible);
        Assert.Contains(RowOf(HarnessTool.ListAgents), visible);
        Assert.DoesNotContain(RowOf(HarnessTool.StopAgent), visible);
        Assert.DoesNotContain(RowOf(HarnessTool.TellAgent), visible);
        Assert.All(visible, i => Assert.Equal(ActionPolicy.Ask, Mixed().RuleFor(SettingsRows.ToolAt(i)).Policy));
    }

    [Fact]
    public void Visible_rows_keep_their_real_order()
    {
        var visible = SettingsTabs.Visible(SettingsTabs.Claude, Mixed());

        Assert.Equal(visible.Order(), visible);
    }
}
