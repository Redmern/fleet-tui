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

    private volatile Endpoint? _third;

    private RemoteChannel FarOpen(string host, string token)
    {
        _farOpened.Enqueue(host);
        if (_third is not { } third)
        {
            throw new IOException("the far machine has no ssh in this test");
        }

        var stream = third.ConnectAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        return new RemoteChannel(stream, null, stream);
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

    private const string Third = "@third";

    private void PlantViewOfThirdMachine()
    {
        _farDaemon.Model.Spawn(Third, string.Empty, ["remote", "red@third"]);
        _farDaemon.Model.Workspace(Third)!.RemoteHost = "red@third";
    }

    private static IReadOnlyList<string> Names(ControlResponse response) => [.. (response.Workspaces ?? []).Select(w => w.Name)];

    [Fact]
    public async Task A_remote_view_is_not_listed_the_workspaces_of_a_third_machine()
    {
        PlantViewOfThirdMachine();
        var (far, _, _, _) = await ShowingHomelabAsync();

        var listed = await far.RequestAsync(new ControlRequest
        {
            Op = "list-workspaces",
            Client = _farDaemon.Model.Clients.Single(c => c.Label is not null).Id,
        });

        Assert.Contains("homelab", Names(listed));
        Assert.DoesNotContain(Third, Names(listed));
    }

    [Fact]
    public async Task A_bridged_connection_is_not_listed_the_workspaces_of_a_third_machine()
    {
        PlantViewOfThirdMachine();
        var viewer = await BridgedControlAsync();
        Assert.True((await viewer.RequestAsync(new ControlRequest { Op = "spawn", Workspace = "homelab", Cwd = ".", Args = ["remote-claude"] })).Ok);

        var listed = await viewer.RequestAsync(new ControlRequest { Op = "list-workspaces" });

        Assert.Contains("homelab", Names(listed));
        Assert.DoesNotContain(Third, Names(listed));
    }

    [Fact]
    public async Task A_client_sitting_at_the_remote_is_listed_the_workspaces_of_a_third_machine()
    {
        PlantViewOfThirdMachine();
        var (far, _, _, _) = await ShowingHomelabAsync();

        var listed = await far.RequestAsync(new ControlRequest { Op = "list-workspaces" });

        Assert.Contains(Third, Names(listed));
    }

    [Fact]
    public async Task The_machine_you_sit_at_does_not_list_a_view_of_a_third_machine_as_a_project_of_the_remote()
    {
        PlantViewOfThirdMachine();
        await ShowingHomelabAsync();

        var remote = Assert.Single(await RemotesAsync());

        Assert.Contains("homelab", remote.Running);
        Assert.DoesNotContain(Third, remote.Running);
        Assert.DoesNotContain(Third, remote.Projects);
    }

    [Theory]
    [InlineData("show")]
    [InlineData("open-window")]
    public async Task A_remote_view_cannot_show_or_open_a_view_of_a_third_machine(string op)
    {
        PlantViewOfThirdMachine();
        var (far, _, _, _) = await ShowingHomelabAsync();
        var linked = _farDaemon.Model.Clients.Single(c => c.Label is not null);

        var response = await far.RequestAsync(new ControlRequest { Op = op, Client = linked.Id, Workspace = Third });

        AssertManagedWhereYouSit(response);
        Assert.NotEqual(Third, linked.Showing);
        Assert.DoesNotContain(linked.Projects, p => p == Third);
    }

    [Fact]
    public async Task A_bridged_connection_cannot_show_a_view_of_a_third_machine()
    {
        PlantViewOfThirdMachine();
        var viewer = await BridgedControlAsync();

        AssertManagedWhereYouSit(await viewer.RequestAsync(new ControlRequest { Op = "show", Client = "c1", Workspace = Third }));
    }

    [Fact]
    public async Task A_client_sitting_at_the_remote_shows_a_view_of_a_third_machine()
    {
        PlantViewOfThirdMachine();
        await using var seated = await DaemonTests.TestClient.ConnectAsync(_far, ClientRoles.Attach, 80, 24, null);

        var response = await seated.RequestAsync(new ControlRequest { Op = "show", Client = seated.Id, Workspace = Third });

        Assert.True(response.Ok, response.Error);
        Assert.Equal(Third, _farDaemon.Model.Client(seated.Id)!.Showing);
    }

    [Fact]
    public async Task A_window_from_an_ssh_session_does_not_start_on_a_view_of_a_third_machine()
    {
        PlantViewOfThirdMachine();
        Assert.True((await (await ClientAsync(_far, ClientRoles.Control)).RequestAsync(
            new ControlRequest { Op = "spawn", Workspace = "homelab", Cwd = ".", Args = ["remote-claude"] })).Ok);

        await using var ssh = await DaemonTests.TestClient.ConnectAsync(_far, ClientRoles.Attach, 80, 24, Third, bridged: true);

        Assert.Equal("homelab", _farDaemon.Model.Client(ssh.Id)!.Showing);
    }

    [Fact]
    public async Task A_window_that_asks_for_nothing_from_an_ssh_session_starts_on_the_first_own_workspace_not_a_view_of_a_third_machine()
    {
        PlantViewOfThirdMachine();
        Assert.True((await (await ClientAsync(_far, ClientRoles.Control)).RequestAsync(
            new ControlRequest { Op = "spawn", Workspace = "homelab", Cwd = ".", Args = ["remote-claude"] })).Ok);

        await using var ssh = await DaemonTests.TestClient.ConnectAsync(_far, ClientRoles.Attach, 80, 24, null, bridged: true);
        await using var seated = await DaemonTests.TestClient.ConnectAsync(_far, ClientRoles.Attach, 80, 24, null);

        Assert.Equal("homelab", _farDaemon.Model.Client(ssh.Id)!.Showing);
        Assert.Equal(Third, _farDaemon.Model.Client(seated.Id)!.Showing);
    }

    [Fact]
    public async Task A_session_is_not_furnished_with_a_view_of_a_third_machine_for_a_remote_viewer()
    {
        PlantViewOfThirdMachine();
        Assert.True((await (await ClientAsync(_far, ClientRoles.Control)).RequestAsync(
            new ControlRequest { Op = "spawn", Workspace = "homelab", Cwd = ".", Args = ["remote-claude"] })).Ok);

        await using var ssh = await DaemonTests.TestClient.ConnectAsync(
            _far,
            ClientRoles.Attach,
            80,
            24,
            "homelab",
            bridged: true,
            furnish: hello =>
            {
                hello.Window = [new() { Name = "homelab" }, new() { Name = Third }];
                hello.Showing = new() { Name = Third };
            });

        var client = _farDaemon.Model.Client(ssh.Id)!;
        Assert.Equal("homelab", client.Showing);
        Assert.DoesNotContain(Third, client.Projects);
    }

    [Fact]
    public async Task A_session_is_furnished_with_a_view_of_a_third_machine_for_a_client_sitting_at_the_remote()
    {
        PlantViewOfThirdMachine();

        await using var seated = await DaemonTests.TestClient.ConnectAsync(
            _far,
            ClientRoles.Attach,
            80,
            24,
            null,
            furnish: hello =>
            {
                hello.Window = [new() { Name = Third }];
                hello.Showing = new() { Name = Third };
            });

        Assert.Equal(Third, _farDaemon.Model.Client(seated.Id)!.Showing);
    }

    [Fact]
    public async Task Nothing_the_machine_you_sit_at_does_through_its_view_of_the_remote_lands_on_a_third_machine()
    {
        PlantViewOfThirdMachine();
        var (far, home, window, _) = await ShowingHomelabAsync();
        var linked = _farDaemon.Model.Clients.Single(c => c.Label is not null);
        var panesOfThird = _farDaemon.Model.PanesIn(Third).Count();

        await home.RequestAsync(new ControlRequest { Op = "show-remote", Client = window.Id, Host = "red@far", Workspace = Third });
        await window.SendCommandAsync("show", Third);
        await window.SendCommandAsync("next-workspace");
        await window.SendCommandAsync("menu", "list-agents");
        var listed = await far.RequestAsync(new ControlRequest { Op = "list-workspaces", Client = linked.Id });
        await Eventually(async () => (await far.RequestAsync(new ControlRequest { Op = "ping" })).Ok);

        Assert.DoesNotContain(Third, Names(listed));
        Assert.NotEqual(Third, linked.Showing);
        Assert.DoesNotContain(Third, linked.Projects);
        Assert.Equal(panesOfThird, _farDaemon.Model.PanesIn(Third).Count());
        Assert.DoesNotContain(Third, Assert.Single(await RemotesAsync()).Running);
    }

    private static string ThirdWorkspace => FleetDaemon.RemoteWorkspace(Environment.MachineName);

    private async Task<DaemonTests.TestClient> FarLinkedToThirdAsync()
    {
        _third = NewEndpoint();
        Start(_third, new FakePanes());
        await using (var third = await DaemonTests.TestClient.ConnectAsync(_third, ClientRoles.Control, 0, 0, null))
        {
            Assert.True((await third.RequestAsync(new ControlRequest { Op = "spawn", Workspace = "proj", Cwd = ".", Args = ["third-claude"] })).Ok);
        }

        var seated = await ClientAsync(_far, ClientRoles.Control);
        Assert.True((await seated.RequestAsync(new ControlRequest { Op = "remote-connect", Host = "red@third" })).Ok);
        await Eventually(async () => (await seated.RequestAsync(new ControlRequest { Op = "list-remotes" }))
            .Remotes?.SingleOrDefault() is { State: RemoteLink.Connected });
        Assert.True((await (await ClientAsync(_far, ClientRoles.Control)).RequestAsync(
            new ControlRequest { Op = "spawn", Workspace = "homelab", Cwd = ".", Args = ["remote-claude"] })).Ok);
        return seated;
    }

    [Fact]
    public async Task A_saved_window_entry_on_another_machine_is_not_furnished_to_a_viewer_though_the_remote_is_linked_to_it()
    {
        await FarLinkedToThirdAsync();

        await using var ssh = await DaemonTests.TestClient.ConnectAsync(
            _far, ClientRoles.Attach, 80, 24, "homelab", bridged: true,
            furnish: hello => hello.Window = [new() { Name = "homelab" }, new() { Name = "proj", Host = "red@third" }]);

        Assert.Null(_farDaemon.Model.Workspace(ThirdWorkspace));
        Assert.DoesNotContain(ThirdWorkspace, _farDaemon.Model.Client(ssh.Id)!.Projects);
    }

    [Fact]
    public async Task A_saved_showing_on_another_machine_is_not_furnished_to_a_viewer_though_the_remote_shows_it_to_someone()
    {
        var seated = await FarLinkedToThirdAsync();
        await using var sitting = await DaemonTests.TestClient.ConnectAsync(_far, ClientRoles.Attach, 80, 24, "homelab");
        Assert.True((await seated.RequestAsync(new ControlRequest { Op = "show-remote", Client = sitting.Id, Host = "red@third", Workspace = "proj" })).Ok);
        await Eventually(() => Task.FromResult(_farDaemon.Model.Client(sitting.Id)!.Showing == ThirdWorkspace));

        await using var ssh = await DaemonTests.TestClient.ConnectAsync(
            _far, ClientRoles.Attach, 80, 24, "homelab", bridged: true,
            furnish: hello => hello.Showing = new() { Name = "proj", Host = "red@third" });

        Assert.Equal("homelab", _farDaemon.Model.Client(ssh.Id)!.Showing);
    }

    [Fact]
    public async Task A_saved_window_entry_on_another_machine_is_furnished_to_a_client_sitting_at_the_remote()
    {
        await FarLinkedToThirdAsync();

        await using var sitting = await DaemonTests.TestClient.ConnectAsync(
            _far, ClientRoles.Attach, 80, 24, "homelab",
            furnish: hello =>
            {
                hello.Window = [new() { Name = "homelab" }, new() { Name = "proj", Host = "red@third" }];
                hello.Showing = new() { Name = "proj", Host = "red@third" };
            });

        Assert.Equal(ThirdWorkspace, _farDaemon.Model.Client(sitting.Id)!.Showing);
    }

    [Fact]
    public async Task The_show_command_of_a_viewer_for_a_view_of_a_third_machine_is_ignored()
    {
        PlantViewOfThirdMachine();
        Assert.True((await (await ClientAsync(_far, ClientRoles.Control)).RequestAsync(
            new ControlRequest { Op = "spawn", Workspace = "homelab", Cwd = ".", Args = ["remote-claude"] })).Ok);
        await using var ssh = await DaemonTests.TestClient.ConnectAsync(_far, ClientRoles.Attach, 80, 24, "homelab", bridged: true);

        await ssh.SendCommandAsync("show", Third);
        Assert.True((await ssh.RequestAsync(new ControlRequest { Op = "ping" })).Ok);

        Assert.Equal("homelab", _farDaemon.Model.Client(ssh.Id)!.Showing);
    }

    [Fact]
    public async Task The_show_command_of_a_client_sitting_at_the_remote_for_a_view_of_a_third_machine_shows_it()
    {
        PlantViewOfThirdMachine();
        Assert.True((await (await ClientAsync(_far, ClientRoles.Control)).RequestAsync(
            new ControlRequest { Op = "spawn", Workspace = "homelab", Cwd = ".", Args = ["remote-claude"] })).Ok);
        await using var sitting = await DaemonTests.TestClient.ConnectAsync(_far, ClientRoles.Attach, 80, 24, "homelab");

        await sitting.SendCommandAsync("show", Third);
        Assert.True((await sitting.RequestAsync(new ControlRequest { Op = "ping" })).Ok);

        Assert.Equal(Third, _farDaemon.Model.Client(sitting.Id)!.Showing);
    }

    [Theory]
    [MemberData(nameof(ViewerRefusedOps))]
    public async Task A_menu_the_remote_opened_over_no_project_for_a_remote_view_cannot_reach_a_third_machine(string op)
    {
        await using var viewer = await DaemonTests.TestClient.ConnectAsync(
            _far, ClientRoles.Attach, 80, 24, null, furnish: hello => hello.Label = RemoteLink.Label("laptop"));
        await viewer.WaitForFramesAsync(0);
        await viewer.SendCommandAsync("menu");
        await Eventually(() => Task.FromResult(_farPanes.ByProgram("fleet") is not null));
        Assert.NotNull(_farDaemon.Model.Client(viewer.Id)!.Overlay);
        var request = ThirdMachine(op);
        request.Caller = _farPanes.ByProgram("fleet")!.Env[FleetDaemon.PaneVariable];

        AssertManagedWhereYouSit(await (await ClientAsync(_far, ClientRoles.Control)).RequestAsync(request));

        Assert.Empty(_farOpened);
    }

    private string PlantedPaneOfThirdMachine()
    {
        PlantViewOfThirdMachine();
        return _farDaemon.Model.PanesIn(Third).Single();
    }

    [Fact]
    public async Task A_bridged_connection_is_not_listed_the_panes_of_a_view_of_a_third_machine()
    {
        var plantedPane = PlantedPaneOfThirdMachine();
        var viewer = await BridgedControlAsync();
        Assert.True((await viewer.RequestAsync(new ControlRequest { Op = "spawn", Workspace = "homelab", Cwd = ".", Args = ["remote-claude"] })).Ok);

        var listed = await viewer.RequestAsync(new ControlRequest { Op = "list-panes" });

        Assert.Contains(listed.Panes!, p => p.Session == "homelab");
        Assert.DoesNotContain(listed.Panes!, p => p.Id == plantedPane);
    }

    [Fact]
    public async Task A_remote_view_is_not_listed_the_panes_of_a_view_of_a_third_machine()
    {
        var plantedPane = PlantedPaneOfThirdMachine();
        var (far, _, _, _) = await ShowingHomelabAsync();

        var listed = await far.RequestAsync(new ControlRequest
        {
            Op = "list-panes",
            Client = _farDaemon.Model.Clients.Single(c => c.Label is not null).Id,
        });

        Assert.Contains(listed.Panes!, p => p.Session == "homelab");
        Assert.DoesNotContain(listed.Panes!, p => p.Id == plantedPane);
    }

    [Fact]
    public async Task A_client_sitting_at_the_remote_is_listed_the_panes_of_a_view_of_a_third_machine()
    {
        var plantedPane = PlantedPaneOfThirdMachine();
        var (far, _, _, _) = await ShowingHomelabAsync();

        var listed = await far.RequestAsync(new ControlRequest { Op = "list-panes" });

        Assert.Contains(listed.Panes!, p => p.Id == plantedPane);
    }

    [Theory]
    [InlineData("get-text")]
    [InlineData("send-text")]
    public async Task A_bridged_connection_cannot_read_or_type_into_a_view_of_a_third_machine(string op)
    {
        var plantedPane = PlantedPaneOfThirdMachine();
        var viewer = await BridgedControlAsync();

        AssertManagedWhereYouSit(await viewer.RequestAsync(new ControlRequest { Op = op, Pane = plantedPane, Text = "x" }));
    }

    [Theory]
    [InlineData("get-text")]
    [InlineData("send-text")]
    public async Task A_client_sitting_at_the_remote_reads_and_types_into_a_view_of_a_third_machine_pane_as_before(string op)
    {
        var plantedPane = PlantedPaneOfThirdMachine();
        var seated = await ClientAsync(_far, ClientRoles.Control);

        var response = await seated.RequestAsync(new ControlRequest { Op = op, Pane = plantedPane, Text = "x" });

        Assert.NotEqual(ViewerHints.ManagedWhereYouSit, response.Error);
    }

    [Theory]
    [InlineData("get-text")]
    [InlineData("send-text")]
    public async Task A_remote_view_still_reads_and_types_into_the_panes_of_the_remotes_own_projects(string op)
    {
        var (far, _, _, _) = await ShowingHomelabAsync();
        var linked = _farDaemon.Model.Clients.Single(c => c.Label is not null).Id;
        var own = (await far.RequestAsync(new ControlRequest { Op = "list-panes" })).Panes!.Single(p => p.Session == "homelab").Id;

        var response = await far.RequestAsync(new ControlRequest { Op = op, Pane = own, Client = linked, Text = "x" });

        Assert.True(response.Ok, response.Error);
    }

    public enum Way
    {
        BridgedConnection,
        LinkClient,
    }

    private async Task<(DaemonTests.TestClient Asking, string? Client)> ViewerAsync(Way way)
    {
        if (way == Way.BridgedConnection)
        {
            var bridged = await BridgedControlAsync();
            Assert.True((await bridged.RequestAsync(new ControlRequest { Op = "spawn", Workspace = "homelab", Cwd = ".", Args = ["own"] })).Ok);
            return (bridged, null);
        }

        var (far, _, _, _) = await ShowingHomelabAsync();
        return (far, _farDaemon.Model.Clients.Single(c => c.Label is not null).Id);
    }

    private async Task AssertPaneOfThirdMachineStaysHiddenAsync(DaemonTests.TestClient asking, string? client, string plantedPane)
    {
        var read = await asking.RequestAsync(new ControlRequest { Op = "get-text", Pane = plantedPane, Client = client });
        AssertManagedWhereYouSit(read);
        var listed = await asking.RequestAsync(new ControlRequest { Op = "list-panes", Client = client });
        Assert.DoesNotContain(listed.Panes!, p => p.Id == plantedPane);
    }

    [Theory]
    [InlineData(Way.BridgedConnection)]
    [InlineData(Way.LinkClient)]
    public async Task A_viewer_cannot_move_the_pane_of_a_view_of_a_third_machine_into_its_own_workspace(Way way)
    {
        var plantedPane = PlantedPaneOfThirdMachine();
        var (asking, client) = await ViewerAsync(way);

        AssertManagedWhereYouSit(await asking.RequestAsync(
            new ControlRequest { Op = "move", Pane = plantedPane, Workspace = "homelab", Client = client }));

        await AssertPaneOfThirdMachineStaysHiddenAsync(asking, client, plantedPane);
    }

    [Theory]
    [InlineData(Way.BridgedConnection)]
    [InlineData(Way.LinkClient)]
    public async Task A_viewer_cannot_split_the_pane_of_a_view_of_a_third_machine_beside_its_own_pane(Way way)
    {
        var plantedPane = PlantedPaneOfThirdMachine();
        var (asking, client) = await ViewerAsync(way);
        var own = (await asking.RequestAsync(new ControlRequest { Op = "list-panes", Client = client })).Panes!.First(p => p.Session == "homelab").Id;

        AssertManagedWhereYouSit(await asking.RequestAsync(
            new ControlRequest { Op = "split", Pane = own, MovePane = plantedPane, Direction = "right", Percent = 50, Client = client }));

        await AssertPaneOfThirdMachineStaysHiddenAsync(asking, client, plantedPane);
    }

    [Theory]
    [InlineData(Way.BridgedConnection)]
    [InlineData(Way.LinkClient)]
    public async Task A_viewer_cannot_move_an_own_pane_into_a_view_of_a_third_machine(Way way)
    {
        PlantViewOfThirdMachine();
        var (asking, client) = await ViewerAsync(way);
        var own = (await asking.RequestAsync(new ControlRequest { Op = "list-panes", Client = client })).Panes!.First(p => p.Session == "homelab").Id;

        AssertManagedWhereYouSit(await asking.RequestAsync(
            new ControlRequest { Op = "move", Pane = own, Workspace = Third, Client = client }));
    }

    [Fact]
    public async Task A_client_sitting_at_the_remote_moves_the_pane_of_a_view_of_a_third_machine_as_before()
    {
        var plantedPane = PlantedPaneOfThirdMachine();
        var (far, _, _, _) = await ShowingHomelabAsync();

        var moved = await far.RequestAsync(new ControlRequest { Op = "move", Pane = plantedPane, Workspace = "homelab" });

        Assert.True(moved.Ok, moved.Error);
        Assert.Contains((await far.RequestAsync(new ControlRequest { Op = "list-panes" })).Panes!, p => p.Id == plantedPane && p.Session == "homelab");
    }

    [Theory]
    [InlineData(Way.BridgedConnection)]
    [InlineData(Way.LinkClient)]
    public async Task A_viewer_still_moves_and_splits_the_panes_of_the_remotes_own_projects(Way way)
    {
        var (asking, client) = await ViewerAsync(way);
        var spawn = await (await ClientAsync(_far, ClientRoles.Control)).RequestAsync(
            new ControlRequest { Op = "spawn", Workspace = "homelab", Cwd = ".", Args = ["second"] });
        Assert.True(spawn.Ok, spawn.Error);
        var panes = (await asking.RequestAsync(new ControlRequest { Op = "list-panes", Client = client })).Panes!.Where(p => p.Session == "homelab").ToList();
        Assert.Equal(2, panes.Count);

        var split = await asking.RequestAsync(
            new ControlRequest { Op = "split", Pane = panes[0].Id, MovePane = panes[1].Id, Direction = "right", Percent = 50, Client = client });
        var moved = await asking.RequestAsync(new ControlRequest { Op = "move", Pane = panes[1].Id, Workspace = "elsewhere", Client = client });

        Assert.True(split.Ok, split.Error);
        Assert.True(moved.Ok, moved.Error);
    }
}
