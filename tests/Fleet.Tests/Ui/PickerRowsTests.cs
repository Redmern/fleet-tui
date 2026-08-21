using Fleet.Features.Agents.RemoveAgent.Models;
using Fleet.Ui;
using Fleet.Ui.Constants;
using Fleet.Ui.Models;

namespace Fleet.Tests.Ui;

public class PickerRowsTests
{
    private static IReadOnlyList<FleetRow> Rows(IReadOnlyList<PickerEntry> entries) =>
        PickerKeys.Rows(entries, PickerKeys.For([.. entries.Select(e => e.Label)]));

    [Fact]
    public void The_key_leads_the_row_in_its_own_colour()
    {
        var row = Rows([new PickerEntry("stop", "Stop the agent")])[0];

        Assert.Equal(FleetTones.Key, row.Spans[0].Tone);
        Assert.Equal("s  ", row.Spans[0].Text);
    }

    [Fact]
    public void Keywords_are_padded_so_the_descriptions_line_up()
    {
        var rows = Rows([new PickerEntry("hide", "a"), new PickerEntry("forget", "b")]);

        Assert.Equal(rows[0].Spans[1].Text.Length, rows[1].Spans[1].Text.Length);
    }

    [Fact]
    public void The_description_is_a_trailing_span_so_it_hugs_the_right_edge()
    {
        var row = Rows([new PickerEntry("delete", "Remove the agent and delete its worktree")])[0];

        Assert.NotNull(row.Trailing);
        Assert.Contains("delete its worktree", row.Trailing![0].Text);
        Assert.Equal(FleetTones.Muted, row.Trailing[0].Tone);
    }

    [Fact]
    public void An_entry_without_a_description_has_nothing_trailing()
    {
        var row = Rows([new PickerEntry("develop")])[0];

        Assert.Null(row.Trailing);
        Assert.Contains("develop", row.Text);
    }

    [Fact]
    public void The_manage_menu_keys_read_as_mnemonics_of_their_keywords()
    {
        var keys = PickerKeys.For(AgentDisposal.Entries);

        Assert.Equal(["o", "p", "h", "r", "s", "f", "d"], keys);
    }
}
