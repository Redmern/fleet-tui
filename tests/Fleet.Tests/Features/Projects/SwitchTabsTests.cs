using Fleet.Features.Projects.SwitchProject;
using Fleet.Ports.Remotes.Enums;
using Fleet.Ports.Remotes.Models;
using Fleet.Ui.Models;

namespace Fleet.Tests.Features.Projects;

public sealed class SwitchTabsTests
{
    private static readonly RemoteMachine Homelab =
        new("user@homelab", "homelab", RemoteState.Connected, ["api", "scraper", "web"], Running: ["web", "api"]);

    private static SwitchTabs WithHomelab() =>
        SwitchTabs.For(
            [new PickerEntry("alpha", "open"), new PickerEntry("fleet", "this window"), new PickerEntry("pc", "this window")],
            ["fleet", "PC"],
            [Homelab]);

    [Fact]
    public void Open_lists_this_windows_projects_then_the_running_remote_ones()
    {
        var tabs = WithHomelab();

        Assert.Equal(SwitchTabs.OpenTitle, tabs.Tabs[SwitchTabs.Open].Title);
        Assert.Equal(
            [("fleet", "this window"), ("pc", "this window"), ("api", "homelab · open"), ("web", "homelab · open")],
            tabs.Tabs[SwitchTabs.Open].Entries.Select(e => (e.Label, e.Detail)));
    }

    [Fact]
    public void Open_targets_line_up_with_its_entries()
    {
        var tabs = WithHomelab();

        Assert.Equal(
            [new SwitchTarget("fleet"), new SwitchTarget("pc"), new SwitchTarget("api", "user@homelab"), new SwitchTarget("web", "user@homelab")],
            tabs.Targets[SwitchTabs.Open]);
    }

    [Fact]
    public void Remote_tabs_follow_this_machine_after_open_and_all()
    {
        var tabs = WithHomelab();

        Assert.Equal(["Open", "All", "this machine", "homelab"], tabs.Tabs.Select(t => t.Title));
        Assert.Equal(2, tabs.ThisMachine);
        Assert.Equal(3, tabs.MachineTab(0));
        Assert.Equal(tabs.Tabs.Count, tabs.Targets.Count);
    }

    [Fact]
    public void Without_remotes_the_tabs_are_open_and_this_machine()
    {
        var tabs = SwitchTabs.For([new PickerEntry("fleet"), new PickerEntry("pc")], ["pc"], []);

        Assert.Equal(["Open", "this machine"], tabs.Tabs.Select(t => t.Title));
        Assert.Equal(1, tabs.ThisMachine);
        Assert.Equal([new SwitchTarget("pc")], tabs.Targets[SwitchTabs.Open]);
    }

    [Fact]
    public void The_switcher_starts_on_open_with_the_current_project_selected()
    {
        var tabs = WithHomelab();

        Assert.Equal((SwitchTabs.Open, 1), tabs.Start(t => t is { Host: null, Project: "pc" }, (tabs.ThisMachine, 2)));
        Assert.Equal((SwitchTabs.Open, 2), tabs.Start(t => t.Host == Homelab.Host, (tabs.MachineTab(0), 0)));
    }

    [Fact]
    public void The_switcher_starts_on_the_first_open_entry_when_the_current_one_is_not_listed()
    {
        var tabs = WithHomelab();

        Assert.Equal((SwitchTabs.Open, 0), tabs.Start(t => t is { Host: null, Project: "alpha" }, (tabs.ThisMachine, 0)));
    }

    [Fact]
    public void A_nicknamed_machine_shows_its_nickname_in_the_tab_and_details()
    {
        var tabs = SwitchTabs.For([], [], [Homelab with { Nickname = "lab" }]);

        Assert.Equal("lab", tabs.Tabs[tabs.MachineTab(0)].Title);
        Assert.Equal(["lab · open", "lab · open"], tabs.Tabs[SwitchTabs.Open].Entries.Select(e => e.Detail));
    }

    [Fact]
    public void An_empty_open_tab_falls_back_to_the_old_start()
    {
        var tabs = SwitchTabs.For([new PickerEntry("fleet"), new PickerEntry("pc")], [], []);

        Assert.Empty(tabs.Tabs[SwitchTabs.Open].Entries);
        Assert.Equal((tabs.ThisMachine, 1), tabs.Start(t => t.Project == "pc", (tabs.ThisMachine, 1)));
    }

    [Fact]
    public void This_machine_and_every_remote_end_with_a_new_project_entry_on_the_new_project_key()
    {
        var tabs = SwitchTabs.For([new PickerEntry("fleet")], [], [Homelab], "n");

        foreach (var tab in (int[])[tabs.ThisMachine, tabs.MachineTab(0)])
        {
            Assert.Equal(new PickerEntry(SwitchTabs.NewLabel, string.Empty, "n"), tabs.Tabs[tab].Entries[^1]);
            Assert.True(tabs.Targets[tab][^1].IsNew);
            Assert.Equal(tabs.Tabs[tab].Entries.Count, tabs.Targets[tab].Count);
        }

        Assert.Null(tabs.Targets[tabs.ThisMachine][^1].Host);
        Assert.Equal(Homelab.Host, tabs.Targets[tabs.MachineTab(0)][^1].Host);
    }

    [Fact]
    public void Open_and_all_offer_no_new_project_entry()
    {
        var tabs = WithHomelab();

        Assert.DoesNotContain(tabs.Targets[SwitchTabs.Open], t => t.IsNew);
        Assert.DoesNotContain(tabs.Targets[1], t => t.IsNew);
    }

    [Fact]
    public void A_remote_without_projects_is_not_a_dead_end()
    {
        var empty = new RemoteMachine("user@fresh", "fresh", RemoteState.Connected, []);
        var tabs = SwitchTabs.For([], [], [empty]);

        var entry = Assert.Single(tabs.Tabs[tabs.MachineTab(0)].Entries);
        Assert.Equal(SwitchTabs.NewLabel, entry.Label);
        Assert.Equal(new SwitchTarget(string.Empty, "user@fresh", IsNew: true), Assert.Single(tabs.Targets[tabs.MachineTab(0)]));
    }
}
