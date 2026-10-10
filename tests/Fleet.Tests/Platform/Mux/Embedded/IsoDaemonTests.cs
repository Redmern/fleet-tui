using Fleet.Platform.Mux.Embedded;
using Fleet.Platform.Mux.Embedded.Daemon;
using Fleet.Platform.Mux.Embedded.Protocol;
using Fleet.Shared;
using Fleet.Shared.Iso;
using Fleet.Shared.Iso.Models;

namespace Fleet.Tests.Platform.Mux.Embedded;

public sealed class IsoDaemonTests : IAsyncLifetime
{
    private const string Origin = "10.0.0.5";

    private const string Worktree = "/work/acme-portal/login";

    private readonly FakePanes _panes = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly List<Wire> _wires = [];
    private readonly List<(string Project, IReadOnlyList<string> Keys)> _dismissed = [];
    private IsoConfig _iso = IsoConfig.Off with { On = true };
    private Endpoint _endpoint = null!;
    private Task _running = Task.CompletedTask;

    public Task InitializeAsync()
    {
        var name = $"fleet-iso-{Guid.NewGuid():N}"[..20];
        _endpoint = new Endpoint(OperatingSystem.IsWindows() ? name : Path.Combine(Path.GetTempPath(), name + ".sock"));
        var daemon = new FleetDaemon(new DaemonOptions
        {
            Endpoint = _endpoint,
            Pty = _panes.NewPty,
            Terminal = _panes.NewTerminal,
            FleetExecutable = "fleet",
            Iso = () => _iso,
            SavedProjects = () => ["acme-portal"],
            Worktrees = p => p == "acme-portal" ? [Worktree] : [],
            Notices = () =>
            [
                new NoticeDto
                {
                    Project = "acme-portal",
                    Key = $"NeedsInput|{PathKey.For(Worktree)}",
                    Kind = "NeedsInput",
                    Worktree = Worktree,
                    Agent = "acme-portal / login",
                    Message = "asks about the ACME invoice",
                    Host = "acme-box",
                },
            ],
            DismissNotices = (project, keys) => _dismissed.Add((project, keys)),
            RemoteOpen = (_, _) => throw new IOException("iso mode should have refused before ssh"),
            IsoSweepEvery = TimeSpan.FromMilliseconds(20),
        });
        _running = daemon.RunAsync(_stop.Token);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        foreach (var wire in _wires)
        {
            wire.Dispose();
        }

        await _stop.CancelAsync();
        await _running;
    }

    private async Task<(Wire Wire, string Client)> ConnectAsync(bool bridged, string role = ClientRoles.Control, string? origin = Origin)
    {
        var wire = new Wire(await _endpoint.ConnectAsync(TimeSpan.FromSeconds(5)));
        _wires.Add(wire);
        await wire.SendAsync(
            MessageType.Hello,
            new Hello { Version = Wire.Version, Role = role, Cols = 60, Rows = 12, Os = "test", Bridged = bridged, Origin = origin },
            WireJsonContext.Default.Hello);

        var welcome = await wire.ReceiveAsync();
        Assert.Equal(MessageType.Welcome, welcome!.Value.Type);
        return (wire, Wire.Read(welcome.Value.Payload, WireJsonContext.Default.Welcome).Client);
    }

    private static async Task<ControlResponse> RequestAsync(Wire wire, ControlRequest request)
    {
        await wire.SendAsync(MessageType.Request, request, WireJsonContext.Default.ControlRequest);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        while (await wire.ReceiveAsync(timeout.Token) is { } message)
        {
            if (message.Type == MessageType.Response)
            {
                return Wire.Read(message.Payload, WireJsonContext.Default.ControlResponse);
            }
        }

        throw new IOException("the daemon closed the connection");
    }

    private async Task<Wire> BridgedAsync() => (await ConnectAsync(bridged: true)).Wire;

    [Fact]
    public async Task A_bridged_client_sees_project_codes_not_names()
    {
        var listed = await RequestAsync(await BridgedAsync(), new ControlRequest { Op = "list-projects" });

        Assert.Equal(["sub1"], listed.Projects);
    }

    [Fact]
    public async Task A_local_client_still_sees_names_in_iso_mode()
    {
        var (local, _) = await ConnectAsync(bridged: false);

        var listed = await RequestAsync(local, new ControlRequest { Op = "list-projects" });

        Assert.Equal(["acme-portal"], listed.Projects);
    }

    [Fact]
    public async Task With_iso_mode_off_a_bridged_client_sees_names()
    {
        _iso = IsoConfig.Off;

        var listed = await RequestAsync(await BridgedAsync(), new ControlRequest { Op = "list-projects" });

        Assert.Equal(["acme-portal"], listed.Projects);
    }

    [Fact]
    public async Task Notices_cross_the_bridge_as_codes_and_fixed_words()
    {
        var notice = Assert.Single((await RequestAsync(await BridgedAsync(), new ControlRequest { Op = "list-notices" })).Notices!);

        Assert.Equal("sub1", notice.Project);
        Assert.Equal("sub1.agent1", notice.Agent);
        Assert.Equal(IsoProjection.Waiting, notice.Message);
        Assert.Equal("NeedsInput|sub1.agent1", notice.Key);
        Assert.Empty(notice.Worktree);
        Assert.Null(notice.Host);
    }

    [Fact]
    public async Task Dismissing_by_code_reaches_the_real_notice()
    {
        var dismissed = await RequestAsync(
            await BridgedAsync(),
            new ControlRequest { Op = "dismiss-notices", Workspace = "sub1", Args = ["NeedsInput|sub1.agent1"] });

        Assert.True(dismissed.Ok, dismissed.Error);
        var (project, keys) = Assert.Single(_dismissed);
        Assert.Equal("acme-portal", project);
        Assert.Equal([$"NeedsInput|{PathKey.For(Worktree)}"], keys);
    }

    [Fact]
    public async Task Panes_cross_the_bridge_without_titles_or_folders()
    {
        var (local, _) = await ConnectAsync(bridged: false);
        var spawned = await RequestAsync(
            local, new ControlRequest { Op = "spawn", Session = "acme-portal", NewWindow = true, Cwd = Worktree, Args = ["claude"] });
        Assert.True(spawned.Ok, spawned.Error);

        var pane = Assert.Single((await RequestAsync(await BridgedAsync(), new ControlRequest { Op = "list-panes" })).Panes!);

        Assert.Equal(spawned.Pane, pane.Id);
        Assert.Equal("sub1", pane.Session);
        Assert.Equal("sub1", pane.Window);
        Assert.Empty(pane.Cwd);
        Assert.Empty(pane.Title);
    }

    [Theory]
    [InlineData("get-text")]
    [InlineData("list-remotes")]
    [InlineData("remote-connect")]
    [InlineData("kill")]
    [InlineData("spawn")]
    [InlineData("send-text")]
    public async Task Ops_that_would_leak_or_reach_out_are_refused(string op)
    {
        var refused = await RequestAsync(await BridgedAsync(), new ControlRequest { Op = op, Pane = "1", Host = "elsewhere" });

        Assert.False(refused.Ok);
        Assert.Equal(IsoProjection.Refused, refused.Error);
    }

    [Fact]
    public async Task A_real_project_name_is_not_an_address()
    {
        var refused = await RequestAsync(await BridgedAsync(), new ControlRequest { Op = "open-project", Workspace = "acme-portal" });

        Assert.False(refused.Ok);
        Assert.Equal(IsoFilter.NoSuchCode, refused.Error);
    }

    [Fact]
    public async Task Status_carries_no_paths_or_machine_name()
    {
        var status = (await RequestAsync(await BridgedAsync(), new ControlRequest { Op = "status" })).Status!;

        Assert.Empty(status.Executable);
        Assert.Null(status.SessionFile);
        Assert.Null(status.Host);
    }

    [Fact]
    public async Task Attach_from_a_host_off_the_allowlist_gets_no_view()
    {
        var (_, client) = await ConnectAsync(bridged: true, ClientRoles.Attach);

        Assert.Empty(client);
    }

    [Fact]
    public async Task Attach_from_an_allowlisted_host_gets_a_view()
    {
        _iso = _iso with { AttachFrom = [Origin] };

        var (_, client) = await ConnectAsync(bridged: true, ClientRoles.Attach);

        Assert.NotEmpty(client);
    }

    [Fact]
    public async Task A_view_ends_once_its_host_leaves_the_allowlist()
    {
        _iso = _iso with { AttachFrom = [Origin] };
        var (viewer, _) = await ConnectAsync(bridged: true, ClientRoles.Attach);
        var (local, _) = await ConnectAsync(bridged: false);
        Assert.Equal(1, (await RequestAsync(local, new ControlRequest { Op = "status" })).Status!.Clients);

        _iso = _iso with { AttachFrom = [] };

        for (var i = 0; i < 100 && (await RequestAsync(local, new ControlRequest { Op = "status" })).Status!.Clients > 0; i++)
        {
            await Task.Delay(20);
        }

        Assert.Equal(0, (await RequestAsync(local, new ControlRequest { Op = "status" })).Status!.Clients);
        Assert.True((await RequestAsync(viewer, new ControlRequest { Op = "ping" })).Ok);
    }

    [Fact]
    public async Task A_bridged_client_cannot_act_on_another_clients_view()
    {
        var (_, local) = await ConnectAsync(bridged: false, ClientRoles.Attach);

        var labelled = await RequestAsync(await BridgedAsync(), new ControlRequest { Op = "show", Client = local, Workspace = "sub1" });

        Assert.False(labelled.Ok);
        Assert.Equal(IsoFilter.Failed, labelled.Error);
    }

    [Fact]
    public async Task A_bridge_without_an_ssh_origin_never_matches_the_allowlist()
    {
        _iso = _iso with { AttachFrom = [Origin] };

        var (_, client) = await ConnectAsync(bridged: true, ClientRoles.Attach, origin: null);

        Assert.Empty(client);
    }

    [Fact]
    public async Task A_local_attach_is_never_restricted()
    {
        var (_, client) = await ConnectAsync(bridged: false, ClientRoles.Attach);

        Assert.NotEmpty(client);
    }

    [Fact]
    public async Task This_machine_opens_no_ssh_connection_in_iso_mode()
    {
        var (local, _) = await ConnectAsync(bridged: false);

        var refused = await RequestAsync(local, new ControlRequest { Op = "remote-connect", Host = "elsewhere" });

        Assert.False(refused.Ok);
        Assert.Equal(IsoGuard.Refusal(IsoGuard.Ssh), refused.Error);
    }

    [Theory]
    [InlineData(ForwardHub.AddOp)]
    [InlineData(ForwardHub.StackStartOp)]
    public async Task This_machine_starts_no_forward_in_iso_mode(string op)
    {
        var (local, _) = await ConnectAsync(bridged: false);

        var refused = await RequestAsync(local, new ControlRequest { Op = op, Host = "elsewhere", Workspace = "acme-portal" });

        Assert.False(refused.Ok);
        Assert.Equal(IsoGuard.Refusal(IsoGuard.Forward), refused.Error);
    }

    [Theory]
    [InlineData("project-configs")]
    [InlineData("viewer-forwards")]
    public async Task Forwarding_ops_from_another_machine_are_refused(string op)
    {
        var refused = await RequestAsync(await BridgedAsync(), new ControlRequest { Op = op });

        Assert.False(refused.Ok);
        Assert.Equal(IsoProjection.Refused, refused.Error);
    }

    // Accepted trade-off: a bridged client in iso mode may ask the machine viewing this one to forward a port.
    [Theory]
    [InlineData(ForwardHub.ViewerForwardOp)]
    [InlineData(ForwardHub.ViewerUnforwardOp)]
    public async Task Viewer_forward_ops_from_another_machine_are_served_in_iso_mode(string op)
    {
        Assert.True(IsoFilter.IsServed(op));

        var served = await RequestAsync(await BridgedAsync(), new ControlRequest { Op = op, Port = 5173 });

        Assert.True(served.Ok, served.Error);
    }

    [Fact]
    public async Task Head_tools_are_not_forwarded_to_another_machine_in_iso_mode()
    {
        var (local, _) = await ConnectAsync(bridged: false);

        var refused = await RequestAsync(
            local, new ControlRequest { Op = FleetDaemon.RemoteHeadOp, Host = "elsewhere", Text = "list_agents" });

        Assert.False(refused.Ok);
        Assert.Equal(IsoProjection.Refused, refused.Error);
    }
}
