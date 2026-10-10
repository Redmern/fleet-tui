using Fleet.Features.Dashboard.ShowDashboard;
using Fleet.Ports.Forwards.Enums;
using Fleet.Ports.Forwards.Models;
using Fleet.Ports.Projects.Models;
using Fleet.Tests.Features.Forwards;

namespace Fleet.Tests.Features.Dashboard;

public sealed class DashboardWebPortsTests
{
    private static readonly Project Web = new("web", "/srv/web", ForwardPorts: [5173]);

    [Fact]
    public void The_project_sees_its_own_forwards_forwarded_ones_first()
    {
        IReadOnlyList<PortForward> all =
        [
            new("box", 9229, null, ForwardState.Waiting, "web"),
            new("box", 5173, 15173, ForwardState.Forwarded, "web"),
            new("box", 8080, 8080, ForwardState.Forwarded, "api"),
        ];

        var ports = DashboardWebPorts.For(all, Web);

        Assert.Equal([5173, 9229], ports.Select(p => p.RemotePort));
        Assert.Equal("web  5173 → localhost:15173  ·  9229 waiting", DashboardWebPorts.Summary(ports));
    }

    [Fact]
    public void A_local_project_offers_its_forward_ports_on_localhost()
    {
        var ports = DashboardWebPorts.For([], Web);

        Assert.Equal("web  localhost:5173", DashboardWebPorts.Summary(ports));
        Assert.Null(DashboardWebPorts.Summary(DashboardWebPorts.For([], new Project("plain", "/srv/plain"))));
    }

    [Fact]
    public async Task Opening_opens_the_first_forwarded_port_and_a_viewer_forward_opens_on_the_viewer()
    {
        var forwards = new FakeForwards();
        var browser = new FakeBrowser();

        Assert.Equal("opened http://localhost:5173", await DashboardWebPorts.OpenAsync(forwards, browser, DashboardWebPorts.For([], Web)));
        Assert.Equal(["http://localhost:5173"], browser.Opened);

        await DashboardWebPorts.OpenAsync(forwards, browser, [new("laptop", 3000, 3000, ForwardState.Forwarded, "web", Viewer: true)]);
        Assert.Equal(["viewer-open 3000"], forwards.Calls);
    }

    [Fact]
    public async Task Nothing_to_open_says_why()
    {
        var said = await DashboardWebPorts.OpenAsync(new FakeForwards(), new FakeBrowser(), []);

        Assert.Contains("forwardPorts", said, StringComparison.Ordinal);
    }
}
