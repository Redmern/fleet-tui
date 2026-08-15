using Fleet.Features.Menu.EditSettings;
using Fleet.Shared.Settings;
using Fleet.Shared.Settings.Enums;
using Fleet.Shared.Settings.Models;
using Fleet.Ui.Constants;

namespace Fleet.Tests.Features.Menu;

public class SettingsRowsTests
{
    [Fact]
    public void The_first_row_is_the_trigger_and_the_rest_are_the_tools()
    {
        var rows = SettingsRows.For(SettingsConfig.Default);

        Assert.Equal(SettingsRows.Count, rows.Count);
        Assert.Contains(SettingsRows.TriggerLabel, rows[0].Text);
        Assert.Contains(",", rows[0].Text);
        Assert.True(SettingsRows.IsTriggerRow(0));
        Assert.Equal(SettingsDefaults.Configurable[0], SettingsRows.ToolAt(1));
    }

    [Fact]
    public void The_policy_is_toned_by_what_it_is()
    {
        var config = SettingsConfig.Default
            .With(HarnessTool.NewAgent, ActionPolicy.Forbid);

        var rows = SettingsRows.For(config);

        var allowRow = rows[SettingsDefaults.Configurable.ToList().IndexOf(HarnessTool.ListAgents) + 1];
        var forbidRow = rows[SettingsDefaults.Configurable.ToList().IndexOf(HarnessTool.NewAgent) + 1];

        Assert.Contains(allowRow.Trailing!, s => s.Tone == FleetTones.Good);
        Assert.Contains(forbidRow.Trailing!, s => s.Tone == FleetTones.Bad);
    }

    [Fact]
    public void The_channel_column_shows_a_dash_unless_the_policy_asks()
    {
        var config = SettingsConfig.Default.With(HarnessTool.NewAgent, ActionPolicy.Allow);

        var allow = SettingsRows.For(config)[SettingsRows.Tools.ToList().IndexOf(HarnessTool.NewAgent) + 1];
        var ask = SettingsRows.For(SettingsConfig.Default)[SettingsRows.Tools.ToList().IndexOf(HarnessTool.StopAgent) + 1];

        Assert.Contains("—", allow.Text);
        Assert.Contains("both", ask.Text);
    }

    [Fact]
    public void Columns_line_up_across_every_row()
    {
        var rows = SettingsRows.For(SettingsConfig.Default);

        var labelWidths = rows.Select(r => r.Spans[0].Text.Length).Distinct();

        Assert.Single(labelWidths);
    }

    [Fact]
    public void Its_policy_keys_read_as_mnemonics()
    {
        Assert.Equal(["a", "s", "f"], SettingsRows.PolicyEntries().Select(e => e.Key));
        Assert.Equal(["b", "d", "c"], SettingsRows.ChannelEntries().Select(e => e.Key));
    }
}
