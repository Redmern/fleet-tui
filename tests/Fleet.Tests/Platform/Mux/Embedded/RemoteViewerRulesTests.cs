using System.Collections.Concurrent;
using Fleet.Platform.Forwards.Models;
using Fleet.Platform.Mux.Embedded;
using Fleet.Platform.Mux.Embedded.Daemon;
using Fleet.Platform.Mux.Embedded.Protocol;
using Fleet.Ports.Forwards.Enums;
using Fleet.Ports.Forwards.Models;
using Fleet.Shared.Viewing;

namespace Fleet.Tests.Platform.Mux.Embedded;

public sealed partial class RemoteLinkTests
{
    private readonly ConcurrentQueue<string> _farOpened = new();

    private RemoteChannel FarOpen(string host, string token)
    {
        _farOpened.Enqueue(host);
        throw new IOException("the far machine has no ssh in this test");
    }

    public static TheoryData<string> ViewerRefusedOps =>
    [
        "list-remotes",
        "remote-connect",
        "remote-answer",
        "remote-disconnect",
        "askpass",
        "show-remote",
        "new-remote-project",
        "remote-notices",
        "remote-dismiss",
        "open-remote-window",
        FleetDaemon.RemoteHeadOp,
        FleetDaemon.RemoteCreateProjectOp,
    ];

    private static ControlRequest ThirdMachine(string op) =>
        new() { Op = op, Host = "red@third", Workspace = "proj", Text = "relay", Answer = "pw", Session = "no-such-token" };

    private async Task<DaemonTests.TestClient> BridgedControlAsync()
    {
        var client = await DaemonTests.TestClient.ConnectAsync(_far, ClientRoles.Control, 0, 0, null, bridged: true);
        _clients.Add(client);
        return client;
    }

    private static void AssertManagedWhereYouSit(ControlResponse response)
    {
        Assert.False(response.Ok);
        Assert.Equal(ViewerHints.ManagedWhereYouSit, response.Error);
    }

    [Theory]
    [MemberData(nameof(ViewerRefusedOps))]
    public async Task A_bridged_connection_cannot_reach_a_third_machine_through_the_remote(string op)
    {
        var viewer = await BridgedControlAsync();

        AssertManagedWhereYouSit(await viewer.RequestAsync(ThirdMachine(op)));

        Assert.Empty(_farOpened);
    }

    [Theory]
    [MemberData(nameof(ViewerRefusedOps))]
    public async Task A_request_for_a_remote_view_client_cannot_reach_a_third_machine_through_the_remote(string op)
    {
        var (far, _, _, _) = await ShowingHomelabAsync();
        var request = ThirdMachine(op);
        request.Client = _farDaemon.Model.Clients.Single(c => c.Label is not null).Id;

        AssertManagedWhereYouSit(await far.RequestAsync(request));

        Assert.Empty(_farOpened);
    }

    [Theory]
    [MemberData(nameof(ViewerRefusedOps))]
    public async Task A_menu_the_remote_opened_for_a_remote_view_cannot_reach_a_third_machine(string op)
    {
        var (far, _, window, _) = await ShowingHomelabAsync();
        await window.SendCommandAsync("menu");
        await Eventually(() => Task.FromResult(_farPanes.ByProgram("fleet") is not null));
        var request = ThirdMachine(op);
        request.Caller = _farPanes.ByProgram("fleet")!.Env[FleetDaemon.PaneVariable];

        AssertManagedWhereYouSit(await far.RequestAsync(request));

        Assert.Empty(_farOpened);
    }

    [Theory]
    [MemberData(nameof(ViewerRefusedOps))]
    public async Task A_client_sitting_at_the_remote_is_not_refused_while_a_remote_view_is_attached(string op)
    {
        var (far, _, _, _) = await ShowingHomelabAsync();

        var response = await far.RequestAsync(ThirdMachine(op));

        Assert.NotEqual(ViewerHints.ManagedWhereYouSit, response.Error);
    }

    [Fact]
    public async Task A_client_sitting_at_the_remote_lists_and_connects_its_own_machines_while_a_remote_view_is_attached()
    {
        var (far, _, _, _) = await ShowingHomelabAsync();

        Assert.True((await far.RequestAsync(new ControlRequest { Op = "list-remotes" })).Ok);
        Assert.True((await far.RequestAsync(new ControlRequest { Op = "remote-connect", Host = "red@third" })).Ok);

        await Eventually(() => Task.FromResult(_farOpened.Contains("red@third")));
    }

    [Fact]
    public async Task A_head_tool_for_another_machine_still_reaches_it_from_a_machine_with_an_ssh_window_attached()
    {
        var (_, home, _, _) = await ShowingHomelabAsync();
        _ = await DaemonTests.TestClient.ConnectAsync(_home, ClientRoles.Attach, 80, 24, "local", bridged: true);

        var head = await home.RequestAsync(new ControlRequest
        {
            Op = FleetDaemon.RemoteHeadOp,
            Host = "red@far",
            Text = "relay",
            Env = new Dictionary<string, string>(),
        });

        Assert.True(head.Ok, head.Error);
        Assert.Equal("relayed", head.Text);
    }

    [Fact]
    public async Task A_remote_window_opens_from_the_machine_you_sit_at()
    {
        var (_, home, window, _) = await ShowingHomelabAsync();

        var opened = await home.RequestAsync(new ControlRequest
        {
            Op = "open-remote-window",
            Client = window.Id,
            Host = "red@far",
            Workspace = "homelab",
        });

        Assert.True(opened.Ok, opened.Error);
        await Eventually(() => Task.FromResult(window.Effects.Any(e => e.Kind == HostEffects.OpenRemote && e.Value == "red@far\nhomelab")));
    }

    [Theory]
    [InlineData(ForwardHub.AddOp)]
    [InlineData(ForwardHub.RemoveOp)]
    [InlineData(ForwardHub.StackStartOp)]
    [InlineData(ForwardHub.StackStopOp)]
    public async Task A_remote_view_cannot_forward_or_stack_on_another_machine(string op)
    {
        var (far, _, _, _) = await ShowingHomelabAsync();
        var request = new ControlRequest
        {
            Op = op,
            Host = "red@third",
            Port = 5173,
            Workspace = "proj",
            Client = _farDaemon.Model.Clients.Single(c => c.Label is not null).Id,
        };

        AssertManagedWhereYouSit(await far.RequestAsync(request));

        Assert.Empty(_farOpened);
    }

    [Theory]
    [InlineData(ForwardHub.AddOp)]
    [InlineData(ForwardHub.StackStartOp)]
    public async Task A_bridged_connection_cannot_forward_or_stack_on_another_machine(string op)
    {
        var viewer = await BridgedControlAsync();

        AssertManagedWhereYouSit(await viewer.RequestAsync(new ControlRequest { Op = op, Host = "red@third", Port = 5173, Workspace = "proj" }));

        Assert.Empty(_farOpened);
    }

    [Fact]
    public async Task A_client_sitting_at_the_remote_still_forwards_on_another_machine_while_a_remote_view_is_attached()
    {
        var (far, _, _, _) = await ShowingHomelabAsync();

        var response = await far.RequestAsync(new ControlRequest { Op = ForwardHub.AddOp, Host = "red@third", Port = 5173 });

        Assert.NotEqual(ViewerHints.ManagedWhereYouSit, response.Error);
    }

    [Fact]
    public async Task Open_port_from_a_remote_view_still_forwards()
    {
        await using var viewer = await DaemonTests.TestClient.ConnectAsync(
            _far, ClientRoles.Attach, 80, 24, null, bridged: true, furnish: hello => hello.Label = RemoteLink.Label("laptop"));

        var asking = viewer.RequestAsync(new ControlRequest { Op = ForwardHub.ViewerForwardOp, Port = 5173, Client = viewer.Id });

        await Eventually(() => Task.FromResult(viewer.Effects.Any(e => e is { Kind: HostEffects.ForwardPort, Reply: not null })));
        var reply = viewer.Effects.First(e => e.Kind == HostEffects.ForwardPort).Reply;
        await using var answering = await DaemonTests.TestClient.ConnectAsync(_far, ClientRoles.Control, 0, 0, null);
        Assert.True((await answering.RequestAsync(new ControlRequest
        {
            Op = ForwardHub.ViewerForwardedOp,
            Reply = reply,
            ForwardResult = new ForwardResultDto { State = ForwardResults.Failed, Url = "http://localhost:5173", Reason = "no" },
        })).Ok);
        Assert.Equal("no", (await asking).ForwardResult?.Reason);
    }
}
