using Fleet.Cli.Composition;
using Fleet.Features.Menu.ShowMenu;
using Fleet.Shared.Keymap.Enums;
using Fleet.Shared.Viewing.Enums;
using Fleet.Shared.Viewing.Models;

namespace Fleet.Tests.Cli;

public class ViewerWiringTests
{
    [Theory]
    [InlineData("view:homelab", ViewKind.View, "homelab")]
    [InlineData("ssh:homelab-box", ViewKind.Ssh, "homelab-box")]
    public void The_mark_fleetd_gave_the_menu_names_its_viewer(string mark, ViewKind kind, string name) =>
        Assert.Equal(new ViewedFrom(kind, name), ViewerWiring.For(mark, iso: false));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("laptop")]
    public void No_mark_means_someone_sitting_at_the_machine_with_or_without_iso(string? mark)
    {
        Assert.Null(ViewerWiring.For(mark, iso: false));
        Assert.Null(ViewerWiring.For(mark, iso: true));
    }

    // With ISO on the remote hides its machine name from a bridged connection, and the label of a
    // view is the ssh host. Neither may show up in a group title.
    [Theory]
    [InlineData("view:red@10.0.0.7", ViewKind.View)]
    [InlineData("ssh:homelab-box", ViewKind.Ssh)]
    public void With_iso_on_the_viewer_keeps_its_kind_but_no_title_names_the_machine(string mark, ViewKind kind)
    {
        var viewer = ViewerWiring.For(mark, iso: true)!;

        Assert.Equal(kind, viewer.Kind);
        foreach (var submenu in (FleetAction[])[FleetAction.None, FleetAction.OpenSettings, FleetAction.OpenFleetConfigMenu])
        {
            Assert.All(RemoteMenus.For(viewer, submenu), s =>
            {
                Assert.DoesNotContain("10.0.0.7", s.Header ?? string.Empty, StringComparison.Ordinal);
                Assert.DoesNotContain("homelab-box", s.Header ?? string.Empty, StringComparison.Ordinal);
            });
        }

        Assert.Equal($"PROJECT on {RemoteMenus.UnnamedMachine}", RemoteMenus.For(viewer, FleetAction.None)[0].Header);
    }
}
