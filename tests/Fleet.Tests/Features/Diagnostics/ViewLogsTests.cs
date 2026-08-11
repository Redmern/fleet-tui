using Fleet.Features.Dashboard.ShowDashboard;
using Fleet.Features.Dashboard.ShowDashboard.Models;
using Fleet.Features.Diagnostics.ViewLogs;
using Fleet.Features.Diagnostics.ViewLogs.Models;
using Fleet.Shared;
using Fleet.Shared.Keymap;
using Fleet.Shared.Keymap.Enums;
using Fleet.Ui;

namespace Fleet.Tests.Features.Diagnostics;

public class ViewLogsTests
{
    private static readonly string[] Lines =
    [
        "2026-08-10T09:00:00.0000000+02:00 [techweb] started agent backend/dev",
        "2026-08-10T09:01:00.0000000+02:00 [other] pulled something",
        "2026-08-10T09:02:00.0000000+02:00 swallowed: IOException: the file was locked",
        "    at Fleet.Platform.Storage.JsonAgentStore.Save(...)",
        "    at Fleet.Features.Agents.NewAgent.NewAgentHandler.HandleAsync(...)",
    ];

    [Fact]
    public void A_timestamped_line_becomes_an_entry()
    {
        var entries = LogParser.Parse(Lines);

        Assert.Equal(3, entries.Count);
        Assert.Equal("started agent backend/dev", entries[0].Message);
        Assert.Equal("techweb", entries[0].Project);
        Assert.StartsWith("08-10 09:00", entries[0].Stamp);
    }

    [Fact]
    public void Lines_without_a_timestamp_are_the_previous_entrys_detail()
    {
        var entries = LogParser.Parse(Lines);

        Assert.Empty(entries[0].Details);
        Assert.Equal(2, entries[2].Details.Count);
        Assert.StartsWith("at Fleet.Platform", entries[2].Details[0]);
    }

    [Fact]
    public void Another_projects_entries_are_left_out_and_the_newest_comes_first()
    {
        var shown = LogParser.For("techweb", LogParser.Parse(Lines));

        Assert.Equal(2, shown.Count);
        Assert.StartsWith("swallowed:", shown[0].Message);
        Assert.Equal("started agent backend/dev", shown[1].Message);
    }

    [Fact]
    public void An_untagged_line_belongs_to_every_project_because_it_is_fleets_own()
    {
        var shown = LogParser.For("anything", LogParser.Parse(Lines));

        Assert.Single(shown);
        Assert.Empty(shown[0].Project);
    }

    [Fact]
    public void A_tag_survives_a_round_trip()
    {
        var (project, message) = LogTag.Split(LogTag.For("techweb", "pulled develop"));

        Assert.Equal("techweb", project);
        Assert.Equal("pulled develop", message);

        Assert.Equal((string.Empty, "no tag here"), LogTag.Split("no tag here"));
    }

    [Fact]
    public void A_row_shows_the_timestamp_then_the_message_and_marks_extra_detail()
    {
        var rows = LogRows.For(LogParser.For("techweb", LogParser.Parse(Lines)));

        Assert.StartsWith("08-10 09:02", rows[0].Text);
        Assert.Contains("swallowed:", rows[0].Text);
        Assert.NotNull(rows[0].Trailing);
        Assert.Contains("2 more", rows[0].Trailing![0].Text);

        Assert.Null(rows[1].Trailing);
    }

    [Fact]
    public void No_entries_says_so_rather_than_showing_an_empty_pane()
    {
        Assert.Equal([LogRows.EmptyHint], LogRows.For([]).Select(r => r.Text));
    }

    [Fact]
    public void An_entry_without_detail_says_that_when_opened()
    {
        var rows = LogRows.Detail(new LogEntry("08-10 09:00", "techweb", "started", []));

        Assert.Contains(LogRows.NoDetail, rows.Select(r => r.Text));
    }

    [Fact]
    public void The_log_key_is_capital_L_because_lowercase_l_switches_tabs()
    {
        var map = Keymap.Default;

        Assert.Equal("L", KeymapDefaults.Bindings[FleetAction.ViewLogs]);
        Assert.NotEqual(map.KeyFor(FleetAction.NextTab), map.KeyFor(FleetAction.ViewLogs));
    }

    [Fact]
    public void The_log_lives_in_the_fleet_menu_rather_than_on_a_dashboard_tab()
    {
        Assert.DoesNotContain(
            FleetAction.ViewLogs,
            DashboardKeys.ScopeFor(DashboardTabs.AgentsTab));

        Assert.DoesNotContain(
            FleetAction.ViewLogs,
            DashboardKeys.ScopeFor(DashboardTabs.RepositoriesTab));
    }
}
