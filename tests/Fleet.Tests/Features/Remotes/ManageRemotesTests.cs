using Fleet.Ports.Forwards.Enums;
using Fleet.Ports.Forwards.Models;
using Fleet.Features.Remotes.ManageRemotes;
using Fleet.Features.Remotes.ManageRemotes.Models;
using Fleet.Ports.Remotes.Enums;
using Fleet.Ports.Remotes.Models;
using Fleet.Ui.Constants;

namespace Fleet.Tests.Features.Remotes;

public sealed class ManageRemotesTests
{
    private static readonly DateTimeOffset Monday = new(2026, 9, 28, 9, 0, 0, TimeSpan.Zero);

    private static readonly RemoteMachine Homelab =
        new("user@homelab", "homelab-01", RemoteState.Connected, ["api", "web"]);

    [Fact]
    public void Merge_lists_live_machines_then_known_ones_most_recent_first()
    {
        var entries = RemoteEntry.Merge(
            [Homelab],
            [
                new KnownRemote("pi@garage", null, Monday),
                new KnownRemote("USER@HOMELAB", "lab", Monday),
                new KnownRemote("me@vps", "vps", Monday.AddDays(1)),
            ]);

        Assert.Equal(["user@homelab", "me@vps", "pi@garage"], entries.Select(e => e.Host));
        Assert.Same(Homelab, entries[0].Live);
        Assert.Equal("lab", entries[0].Known?.Nickname);
        Assert.All(entries.Skip(1), e => Assert.Null(e.Live));
    }

    [Fact]
    public void Label_prefers_the_nickname_then_the_machine_name_then_the_host()
    {
        Assert.Equal("lab", new RemoteEntry("user@homelab", Homelab, new KnownRemote("user@homelab", "lab", Monday)).Label);
        Assert.Equal("homelab-01", new RemoteEntry("user@homelab", Homelab, null).Label);
        Assert.Equal("pi@garage", new RemoteEntry("pi@garage", null, new KnownRemote("pi@garage", null, Monday)).Label);
    }

    [Fact]
    public void Described_names_the_host_next_to_a_nickname()
    {
        Assert.Equal("lab (user@homelab)", new RemoteEntry("user@homelab", null, new KnownRemote("user@homelab", "lab", Monday)).Described);
        Assert.Equal("pi@garage", new RemoteEntry("pi@garage", null, new KnownRemote("pi@garage", null, Monday)).Described);
    }

    [Fact]
    public void A_known_but_not_connected_row_is_muted()
    {
        var row = Assert.Single(ManageRemotesView.Rows(
            RemoteEntry.Merge([], [new KnownRemote("user@homelab", "lab", Monday)])));

        Assert.All(row.Spans.Concat(row.Trailing ?? []), s => Assert.Equal(FleetTones.Muted, s.Tone));
        Assert.Contains("lab", row.Text);
        Assert.Contains("user@homelab", row.Text);
        Assert.Contains(ManageRemotesView.KnownDetail, row.Text);
    }

    [Fact]
    public void A_live_row_shows_the_nickname_with_the_host_muted()
    {
        var row = Assert.Single(ManageRemotesView.Rows(
            RemoteEntry.Merge([Homelab], [new KnownRemote("user@homelab", "lab", Monday)])));

        Assert.Equal("lab", row.Spans[1].Text.Trim());
        Assert.Equal(FleetTones.Normal, row.Spans[1].Tone);
        Assert.Equal("user@homelab", row.Spans[2].Text.Trim());
        Assert.Equal(FleetTones.Muted, row.Spans[2].Tone);
        Assert.Contains("2 projects", row.Text);
    }

    [Fact]
    public void A_live_row_counts_its_forwarded_ports()
    {
        var row = Assert.Single(ManageRemotesView.Rows(RemoteEntry.Merge([Homelab], []), host => host == "user@homelab" ? 2 : 0));

        Assert.Contains("2 projects · 2 ports forwarded", row.Text);
    }

    [Fact]
    public void Port_rows_show_the_url_of_a_forward_and_how_to_forward_the_rest()
    {
        IReadOnlyList<PortForward> all =
        [
            new("user@homelab", 5173, 15173, ForwardState.Forwarded, "web"),
            new("user@homelab", 9229, null, ForwardState.Detected),
            new("user@homelab", 3000, null, ForwardState.Failed, null, "refused"),
            new("other", 5173, 5173, ForwardState.Forwarded),
            new("laptop", 5173, 5173, ForwardState.Forwarded, Viewer: true),
        ];

        var mine = RemotePortsView.Of(all, "user@homelab");
        var rows = RemotePortsView.Rows(mine);

        Assert.Equal(3, mine.Count);
        Assert.Equal(1, RemotePortsView.Forwarded(all, "user@homelab"));
        Assert.Contains("http://localhost:15173", rows[0].Text);
        Assert.Contains("web", rows[0].Text);
        Assert.Contains("f forwards it", rows[1].Text);
        Assert.Equal(FleetTones.Bad, rows[2].Spans[0].Tone);
        Assert.Contains("refused", rows[2].Text);
        Assert.Equal(RemotePortsView.EmptyHint, Assert.Single(RemotePortsView.Rows([])).Text);
    }

    [Fact]
    public void Nothing_live_or_known_shows_the_hint()
    {
        Assert.Equal(ManageRemotesView.EmptyHint, Assert.Single(ManageRemotesView.Rows([])).Text);
    }
}
