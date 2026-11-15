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

    private static Func<string, string?> Environment(params (string Name, string Value)[] variables) =>
        name => variables.Where(v => v.Name == name).Select(v => v.Value).FirstOrDefault();

    [Fact]
    public void The_viewer_is_read_from_the_variable_fleetd_sets_and_from_no_other()
    {
        var onlyTheMark = Environment(("FLEET_VIEWED_FROM", "view:homelab"));
        var everythingElse = Environment(("FLEET_CLIENT", "view:homelab"), ("FLEET_PANE", "ssh:homelab"), ("FLEET_VIEWED", "view:homelab"));

        Assert.Equal("FLEET_VIEWED_FROM", ViewedFrom.Variable);
        Assert.Equal("view:homelab", ViewerWiring.Mark(onlyTheMark));
        Assert.Equal(new ViewedFrom(ViewKind.View, "homelab"), ViewerWiring.Current(onlyTheMark, () => false));
        Assert.Null(ViewerWiring.Mark(everythingElse));
        Assert.Null(ViewerWiring.Current(everythingElse, () => false));
    }

    [Fact]
    public void An_empty_mark_is_no_mark()
    {
        Assert.Null(ViewerWiring.Mark(Environment(("FLEET_VIEWED_FROM", string.Empty))));
        Assert.Null(ViewerWiring.Current(Environment(("FLEET_VIEWED_FROM", string.Empty)), () => true));
    }

    [Fact]
    public void The_viewer_of_the_running_menu_loses_its_name_when_iso_is_on()
    {
        var marked = Environment(("FLEET_VIEWED_FROM", "ssh:homelab-box"));

        Assert.Equal(new ViewedFrom(ViewKind.Ssh, "homelab-box"), ViewerWiring.Current(marked, () => false));
        Assert.Equal(new ViewedFrom(ViewKind.Ssh, RemoteMenus.UnnamedMachine), ViewerWiring.Current(marked, () => true));
    }

    [Fact]
    public void Iso_is_not_read_for_a_menu_nobody_views()
    {
        var asked = 0;

        Assert.Null(ViewerWiring.Current(Environment(), () =>
        {
            asked++;
            return true;
        }));
        Assert.Equal(0, asked);
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
