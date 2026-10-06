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

        Assert.Contains(allowRow.Spans, s => s.Tone == FleetTones.Good);
        Assert.Contains(forbidRow.Spans, s => s.Tone == FleetTones.Bad);
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
    public void The_last_row_switches_status_hooks()
    {
        var last = SettingsRows.Count - 1;

        Assert.True(SettingsRows.IsStatusHooksRow(last));
        Assert.Equal(HarnessTool.None, SettingsRows.ToolAt(last));
        Assert.Contains(SettingsDefaults.StatusHooksLabel, SettingsRows.For(SettingsConfig.Default)[last].Text);
        Assert.Contains("on", SettingsRows.For(SettingsConfig.Default)[last].Text);
        Assert.Contains("off", SettingsRows.For(SettingsConfig.Default.WithStatusHooks(false))[last].Text);
    }

    [Fact]
    public void Its_policy_keys_read_as_mnemonics()
    {
        Assert.Equal(["a", "s", "f"], SettingsRows.PolicyEntries().Select(e => e.Key));
        Assert.Equal(["b", "d", "c"], SettingsRows.ChannelEntries().Select(e => e.Key));
    }

    [Fact]
    public void A_header_names_the_columns_and_lines_up_with_the_rows()
    {
        var config = SettingsConfig.Default;
        var header = SettingsRows.Header(config).Text;
        var rows = SettingsRows.For(config);

        Assert.StartsWith(SettingsRows.NameHeading, header);
        Assert.Contains(SettingsRows.ValueHeading, header);
        Assert.EndsWith(SettingsRows.ChannelHeading, header);

        var first = header.IndexOf('│');
        var second = header.IndexOf('│', first + 1);

        Assert.All(rows, r =>
        {
            Assert.Equal(first, r.Text.IndexOf('│'));
            Assert.Equal(second, r.Text.IndexOf('│', first + 1));
        });
    }

    [Fact]
    public void Every_row_keeps_its_value_in_its_own_toned_span_and_has_no_trailing_part()
    {
        var rows = SettingsRows.For(SettingsConfig.Default);

        Assert.All(rows, r => Assert.Null(r.Trailing));
        Assert.Equal(FleetTones.Key, rows[0].Spans[2].Tone);
        Assert.Contains(SettingsConfig.Default.Trigger, rows[0].Spans[2].Text);
    }

    [Fact]
    public void A_long_trigger_widens_the_value_column_for_every_row()
    {
        var config = SettingsConfig.Default.WithTrigger("ctrl+shift+enter");
        var header = SettingsRows.Header(config).Text;
        var second = header.IndexOf('│', header.IndexOf('│') + 1);

        Assert.All(SettingsRows.For(config), r => Assert.Equal(second, r.Text.IndexOf('│', r.Text.IndexOf('│') + 1)));
    }
}