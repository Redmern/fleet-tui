using Fleet.Platform.Mux.Embedded.Daemon;
using Fleet.Platform.Mux.Embedded.Protocol;
using Fleet.Shared.Viewing;

namespace Fleet.Tests.Platform.Mux.Embedded;

public sealed partial class RemoteLinkTests
{
    [Fact]
    public async Task A_link_announces_the_screens_the_machine_you_sit_at_takes_back()
    {
        await ShowingHomelabAsync();

        var linked = _farDaemon.Model.Clients.Single(c => c.Label is not null);

        Assert.Equal(ViewerScreens.Ids, linked.TakesBack);
    }

    [Fact]
    public async Task A_hand_back_of_remote_machines_for_the_link_client_opens_them_on_the_machine_you_sit_at()
    {
        var (far, _, _, workspace) = await ShowingHomelabAsync();
        var linked = _farDaemon.Model.Clients.Single(c => c.Label is not null).Id;

        var handed = await far.RequestAsync(new ControlRequest { Op = "hand-back", Client = linked, Text = "remotes" });

        Assert.True(handed.Pending);
        await Eventually(() => Task.FromResult(_homePanes.ByProgram("fleet") is { } menu
            && menu.Args.SequenceEqual(["menu", "--project", workspace, "--action", "remotes"])));
    }

    [Fact]
    public async Task A_warm_menu_on_the_remote_hands_remote_machines_back_through_its_pane()
    {
        var (far, _, window, workspace) = await ShowingHomelabAsync();

        await window.SendCommandAsync("menu");
        await Eventually(() => Task.FromResult(_farPanes.ByProgram("fleet") is not null));
        var menuPane = _farPanes.ByProgram("fleet")!.Env[FleetDaemon.PaneVariable];

        var handed = await far.RequestAsync(new ControlRequest { Op = "hand-back", Caller = menuPane, Text = "remotes" });

        Assert.True(handed.Pending);
        await Eventually(() => Task.FromResult(_homePanes.ByProgram("fleet") is { } menu
            && menu.Args.SequenceEqual(["menu", "--project", workspace, "--action", "remotes"])));
    }

    [Fact]
    public async Task An_older_viewer_that_announced_nothing_gets_switch_project_and_notifications_back_but_not_remote_machines()
    {
        await ShowingHomelabAsync();
        var old = await ClientAsync(_far, ClientRoles.Attach, 80, 24, "homelab");
        await old.WaitForFramesAsync(1);
        Assert.True((await old.RequestAsync(new ControlRequest { Op = "set-label", Text = " @old" })).Ok);

        Assert.False((await old.RequestAsync(new ControlRequest { Op = "hand-back", Client = old.Id, Text = "remotes" })).Pending);
        Assert.True((await old.RequestAsync(new ControlRequest { Op = "hand-back", Client = old.Id, Text = "switch-project" })).Pending);
        Assert.True((await old.RequestAsync(new ControlRequest { Op = "hand-back", Client = old.Id, Text = FleetDaemon.NoticesMenu })).Pending);
    }

    [Fact]
    public async Task A_viewer_never_gets_back_an_owned_screen_it_did_not_announce()
    {
        await ShowingHomelabAsync();
        var picky = await ClientAsync(_far, ClientRoles.Attach, 80, 24, "homelab");
        await picky.WaitForFramesAsync(1);
        Assert.True((await picky.RequestAsync(new ControlRequest { Op = "set-label", Text = " @picky", Args = ["notifications", "quit"] })).Ok);

        Assert.False((await picky.RequestAsync(new ControlRequest { Op = "hand-back", Client = picky.Id, Text = "switch-project" })).Pending);
        Assert.False((await picky.RequestAsync(new ControlRequest { Op = "hand-back", Client = picky.Id, Text = "quit" })).Pending);
        Assert.True((await picky.RequestAsync(new ControlRequest { Op = "hand-back", Client = picky.Id, Text = FleetDaemon.NoticesMenu })).Pending);
    }

    [Fact]
    public async Task A_relabel_without_arguments_keeps_what_the_viewer_announced()
    {
        await ShowingHomelabAsync();
        var viewer = await ClientAsync(_far, ClientRoles.Attach, 80, 24, "homelab");
        await viewer.WaitForFramesAsync(1);
        Assert.True((await viewer.RequestAsync(new ControlRequest { Op = "set-label", Text = " @home", Args = [.. ViewerScreens.Ids] })).Ok);

        Assert.True((await viewer.RequestAsync(new ControlRequest { Op = "set-label", Text = " @renamed" })).Ok);
        Assert.True((await viewer.RequestAsync(new ControlRequest { Op = "set-label", Text = " @again", Args = [] })).Ok);

        Assert.True((await viewer.RequestAsync(new ControlRequest { Op = "hand-back", Client = viewer.Id, Text = "remotes" })).Pending);
    }
}
