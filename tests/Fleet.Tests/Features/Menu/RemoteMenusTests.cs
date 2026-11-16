using Fleet.Features.Menu.ShowMenu;
using Fleet.Features.Menu.ShowMenu.Models;
using Fleet.Shared.Keymap;
using Fleet.Shared.Keymap.Enums;
using Fleet.Shared.Viewing;
using Fleet.Shared.Viewing.Enums;
using Fleet.Shared.Viewing.Models;

namespace Fleet.Tests.Features.Menu;

public class RemoteMenusTests
{
    private static readonly ViewedFrom View = new(ViewKind.View, "homelab");

    private static readonly ViewedFrom Ssh = new(ViewKind.Ssh, "homelab-box");

    public static TheoryData<FleetAction> Screens { get; } =
        [FleetAction.None, FleetAction.OpenSettings, FleetAction.OpenFleetConfigMenu];

    public static TheoryData<ViewKind, FleetAction> ViewerScreens { get; } = new()
    {
        { ViewKind.View, FleetAction.None },
        { ViewKind.View, FleetAction.OpenSettings },
        { ViewKind.View, FleetAction.OpenFleetConfigMenu },
        { ViewKind.Ssh, FleetAction.None },
        { ViewKind.Ssh, FleetAction.OpenSettings },
        { ViewKind.Ssh, FleetAction.OpenFleetConfigMenu },
    };

    private static List<(string? Header, FleetAction[] Actions)> Shape(IReadOnlyList<MenuSection> sections) =>
        [.. sections.Select(s => (s.Header, s.Actions.ToArray()))];

    [Fact]
    public void The_main_screen_in_a_remote_view_groups_its_rows_by_the_machine_they_run_on()
    {
        var shape = Shape(RemoteMenus.For(View, FleetAction.None));

        Assert.Equal(["PROJECT on homelab", "MACHINE homelab", "FLEET this machine"], shape.Select(s => s.Header));
        Assert.Equal(
            [FleetAction.QuitFleet, FleetAction.FocusMain, FleetAction.ListAgents, FleetAction.OpenEditor, FleetAction.TodoBoard, FleetAction.BrowseFiles],
            shape[0].Actions);
        Assert.Equal([FleetAction.OpenSettings], shape[1].Actions);
        Assert.Equal(
            [FleetAction.SwitchProject, FleetAction.Notifications, FleetAction.Remotes, FleetAction.SaveSession, FleetAction.ThisMachineSettings],
            shape[2].Actions);
    }

    [Fact]
    public void The_remotes_settings_screen_in_a_remote_view_keeps_only_the_rows_of_the_remote()
    {
        var shape = Shape(RemoteMenus.For(View, FleetAction.OpenSettings));

        Assert.Equal(["PROJECT on homelab", "MACHINE homelab"], shape.Select(s => s.Header));
        Assert.Equal(
            [FleetAction.OpenPort, FleetAction.RebuildDashboard, FleetAction.CleanupProject, FleetAction.HideAllAgents],
            shape[0].Actions);
        Assert.Equal(
            [FleetAction.OpenFleetConfigMenu, FleetAction.ViewLogs, FleetAction.UpdateFleet, FleetAction.ShowVersion, FleetAction.WhatsNew],
            shape[1].Actions);
    }

    [Theory]
    [InlineData(ViewKind.View)]
    [InlineData(ViewKind.Ssh)]
    public void The_remotes_fleet_config_screen_is_unchanged_for_a_viewer(ViewKind kind) =>
        Assert.Same(FleetMenus.FleetConfig, RemoteMenus.For(new ViewedFrom(kind, "homelab"), FleetAction.OpenFleetConfigMenu));

    [Theory]
    [MemberData(nameof(Screens))]
    public void A_menu_opened_by_someone_sitting_at_the_machine_is_the_very_menu_it_was(FleetAction submenu)
    {
        Assert.Same(FleetMenus.For(submenu), RemoteMenus.For(null, submenu));
        Assert.Null(RemoteMenus.Note(null, submenu));
        Assert.All(RemoteMenus.For(null, submenu), s => Assert.DoesNotContain("this machine", s.Header ?? string.Empty, StringComparison.Ordinal));
    }

    [Fact]
    public void The_main_screen_in_an_ssh_window_has_the_project_and_machine_groups_only_and_one_note()
    {
        var shape = Shape(RemoteMenus.For(Ssh, FleetAction.None));

        Assert.Equal(["PROJECT on homelab-box", "MACHINE homelab-box"], shape.Select(s => s.Header));
        Assert.Equal(
            [FleetAction.QuitFleet, FleetAction.FocusMain, FleetAction.ListAgents, FleetAction.OpenEditor, FleetAction.TodoBoard, FleetAction.BrowseFiles],
            shape[0].Actions);
        Assert.Equal([FleetAction.OpenSettings], shape[1].Actions);
        Assert.Equal(ViewerHints.ScreensWhereYouSit, RemoteMenus.Note(Ssh, FleetAction.None));
    }

    [Fact]
    public void The_settings_screen_in_an_ssh_window_is_the_one_of_a_remote_view_with_the_note()
    {
        Assert.Equal(
            Shape(RemoteMenus.For(View with { Name = Ssh.Name }, FleetAction.OpenSettings)).Select(s => (s.Header, string.Join(',', s.Actions))),
            Shape(RemoteMenus.For(Ssh, FleetAction.OpenSettings)).Select(s => (s.Header, string.Join(',', s.Actions))));
        Assert.Equal(ViewerHints.ScreensWhereYouSit, RemoteMenus.Note(Ssh, FleetAction.OpenSettings));
    }

    [Theory]
    [MemberData(nameof(Screens))]
    public void A_remote_view_has_no_note(FleetAction submenu) => Assert.Null(RemoteMenus.Note(View, submenu));

    [Theory]
    [MemberData(nameof(ViewerScreens))]
    public void No_row_of_a_screen_in_an_ssh_window_or_a_remote_view_runs_on_the_wrong_machine(ViewKind kind, FleetAction submenu)
    {
        var viewer = new ViewedFrom(kind, "homelab");

        foreach (var section in RemoteMenus.For(viewer, submenu).Where(s => s.Header is { } h && h.StartsWith("PROJECT", StringComparison.Ordinal)))
        {
            Assert.All(section.Actions, a => Assert.Equal(ActionGroup.Project, ActionGroups.Of(a)));
        }

        foreach (var section in RemoteMenus.For(viewer, submenu).Where(s => s.Header is { } h && h.StartsWith("MACHINE", StringComparison.Ordinal)))
        {
            Assert.All(section.Actions, a => Assert.Equal(ActionGroup.Machine, ActionGroups.Of(a)));
        }

        if (kind == ViewKind.Ssh)
        {
            Assert.DoesNotContain(FleetMenus.Actions(RemoteMenus.For(viewer, submenu)), a => ActionGroups.Of(a) == ActionGroup.ThisMachine);
        }
    }

    [Theory]
    [MemberData(nameof(ViewerScreens))]
    public void No_row_shows_twice_and_no_two_rows_share_a_key(ViewKind kind, FleetAction submenu) =>
        AssertRowsAndKeysUnique(RemoteMenus.For(new ViewedFrom(kind, "homelab"), submenu));

    [Theory]
    [MemberData(nameof(ViewerScreens))]
    public void Every_row_a_viewer_sees_keeps_the_key_it_has_on_the_machine(ViewKind kind, FleetAction submenu)
    {
        var local = FleetMenus.Actions(FleetMenus.Main)
            .Concat(FleetMenus.Actions(FleetMenus.Settings))
            .Concat(FleetMenus.Actions(FleetMenus.FleetConfig))
            .ToHashSet();

        Assert.All(
            FleetMenus.Actions(RemoteMenus.For(new ViewedFrom(kind, "homelab"), submenu)),
            a => Assert.True(local.Contains(a) || a == FleetAction.ThisMachineSettings, $"{a} is not a row of the local menu"));
    }

    // A row without an owner is in no group, so a viewer would silently lose it.
    [Fact]
    public void Every_row_of_the_main_and_settings_screens_has_an_owner_so_no_row_is_lost_for_a_viewer()
    {
        var rows = FleetMenus.Actions(FleetMenus.Main).Concat(FleetMenus.Actions(FleetMenus.Settings)).ToList();

        Assert.All(rows, row => Assert.True(ActionGroups.Of(row) is not null, $"{row} has no owner in ActionGroups"));
    }

    [Theory]
    [InlineData(FleetAction.None)]
    [InlineData(FleetAction.OpenSettings)]
    public void A_remote_view_and_this_machines_settings_together_still_reach_every_row_of_the_local_menu(FleetAction submenu)
    {
        var reachable = FleetMenus.Actions(RemoteMenus.For(View, FleetAction.None))
            .Concat(FleetMenus.Actions(RemoteMenus.For(View, FleetAction.OpenSettings)))
            .Concat(FleetMenus.Actions(RemoteMenus.ThisMachine))
            .ToHashSet();

        Assert.All(FleetMenus.Actions(FleetMenus.For(submenu)), row => Assert.Contains(row, reachable));
    }

    // A setting that flips without a screen must never flip on a hand-back nobody asked for.
    [Fact]
    public void Every_owned_toggle_opens_this_machines_settings_when_handed_back_and_every_other_owned_screen_opens_itself()
    {
        var toggles = Fleet.Shared.Viewing.ViewerScreens.Owned.Where(FleetMenus.IsToggle).ToList();

        Assert.Equal([FleetAction.EditShowMenuKeys, FleetAction.EditButtonHints], toggles);
        Assert.All(Fleet.Shared.Viewing.ViewerScreens.Owned, screen => Assert.Equal(
            FleetActionIds.For(FleetMenus.IsToggle(screen) ? FleetAction.ThisMachineSettings : screen),
            Fleet.Shared.Viewing.ViewerScreens.OpenedWhenHandedBack(FleetActionIds.For(screen))));
    }

    [Fact]
    public void This_machines_settings_screen_has_exactly_its_ten_rows()
    {
        Assert.Equal(
            [
                FleetAction.SaveSession,
                FleetAction.Remotes,
                FleetAction.EditKeybinds,
                FleetAction.EditShowMenuKeys,
                FleetAction.EditButtonHints,
                FleetAction.EditTheme,
                FleetAction.ViewLogs,
                FleetAction.UpdateFleet,
                FleetAction.ShowVersion,
                FleetAction.WhatsNew,
            ],
            FleetMenus.Actions(RemoteMenus.ThisMachine));
        AssertRowsAndKeysUnique(RemoteMenus.ThisMachine);
    }

    [Fact]
    public void A_title_hides_the_name_of_a_machine_that_may_not_be_named()
    {
        var unnamed = RemoteMenus.Unnamed(View);

        Assert.Equal(
            ["PROJECT on the remote machine", "MACHINE the remote machine", "FLEET this machine"],
            RemoteMenus.For(unnamed, FleetAction.None).Select(s => s.Header));
        Assert.DoesNotContain(RemoteMenus.For(unnamed, FleetAction.OpenSettings), s => s.Header!.Contains("homelab", StringComparison.Ordinal));
        Assert.Equal(ViewKind.View, unnamed.Kind);
        Assert.Equal(ViewKind.Ssh, RemoteMenus.Unnamed(Ssh).Kind);
    }

    private static void AssertRowsAndKeysUnique(IReadOnlyList<MenuSection> sections)
    {
        var actions = FleetMenus.Actions(sections);
        var keys = actions.Where(KeymapDefaults.Bindings.ContainsKey).Select(a => KeymapDefaults.Bindings[a]).ToList();

        Assert.Equal(actions.Count, actions.Distinct().Count());
        Assert.Equal(keys.Count, keys.Distinct(StringComparer.Ordinal).Count());
    }
}
