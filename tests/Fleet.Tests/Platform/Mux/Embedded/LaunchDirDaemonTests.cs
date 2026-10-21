using Fleet.Platform.Mux.Embedded.Daemon;
using Fleet.Platform.Mux.Embedded.Protocol;

namespace Fleet.Tests.Platform.Mux.Embedded;

// The folder an attach client was started from reaches the menu the daemon spawns for it, so
// "New project" can default to it even when fleetd itself runs from somewhere else.
[Collection(WallClockCollection.Name)]
public sealed class LaunchDirDaemonTests : IAsyncLifetime
{
    private readonly FakePanes _panes = new();
    private readonly List<DaemonTests.TestClient> _clients = [];
    private readonly CancellationTokenSource _stop = new();
    private readonly string _launch = Path.Combine(Path.GetTempPath(), "fleet-launched-here");
    private Endpoint _endpoint = null!;
    private Task _running = Task.CompletedTask;

    public Task InitializeAsync()
    {
        var name = $"fleet-test-{Guid.NewGuid():N}"[..20];
        _endpoint = new Endpoint(OperatingSystem.IsWindows() ? name : Path.Combine(Path.GetTempPath(), name + ".sock"));
        _running = new FleetDaemon(new DaemonOptions
        {
            Endpoint = _endpoint,
            Pty = _panes.NewPty,
            Terminal = _panes.NewTerminal,
            FleetExecutable = "fleet",
        }).RunAsync(_stop.Token);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        foreach (var client in _clients)
        {
            await client.DisposeAsync();
        }

        await _stop.CancelAsync();
        await _running;
    }

    private async Task<DaemonTests.TestClient> ConnectAsync(string role, string? workspace = null, string? cwd = null)
    {
        var client = await DaemonTests.TestClient.ConnectAsync(
            _endpoint, role, 100, 30, workspace, furnish: hello => hello.Cwd = cwd);
        _clients.Add(client);
        return client;
    }

    [Fact]
    public void Hello_carries_the_cwd_and_an_older_hello_without_it_reads_as_null()
    {
        var sent = new Hello { Role = ClientRoles.Attach, Cwd = "/home/me/code" };

        var read = Wire.Read(
            System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(sent, WireJsonContext.Default.Hello),
            WireJsonContext.Default.Hello);
        var older = Wire.Read("{\"version\":1,\"role\":\"attach\"}"u8.ToArray(), WireJsonContext.Default.Hello);

        Assert.Equal("/home/me/code", read.Cwd);
        Assert.Null(older.Cwd);
    }

    [Fact]
    public async Task The_menu_opened_without_a_workspace_gets_the_clients_folder_not_the_daemons()
    {
        var client = await ConnectAsync(ClientRoles.Attach, cwd: _launch);

        await client.SendCommandAsync("menu");

        await ClipboardImageDaemonTests.Eventually(() => _panes.ByProgram("fleet") is not null);
        Assert.Equal(_launch, _panes.ByProgram("fleet")!.Env[FleetDaemon.LaunchDirVariable]);
        Assert.NotEqual(Environment.CurrentDirectory, _launch);
    }

    [Fact]
    public async Task The_menu_float_in_a_workspace_gets_the_clients_folder()
    {
        var control = await ConnectAsync(ClientRoles.Control);
        Assert.True((await control.RequestAsync(new ControlRequest
        {
            Op = "spawn",
            Session = "techweb",
            NewWindow = true,
            Cwd = ".",
            Args = ["claude"],
        })).Ok);
        var client = await ConnectAsync(ClientRoles.Attach, "techweb", _launch);
        await client.WaitForFramesAsync(1);

        await client.SendCommandAsync("menu", "new-project");

        await ClipboardImageDaemonTests.Eventually(() => _panes.ByProgram("fleet") is not null);
        Assert.Equal(_launch, _panes.ByProgram("fleet")!.Env[FleetDaemon.LaunchDirVariable]);
    }

    [Fact]
    public async Task An_older_client_without_a_cwd_attaches_and_its_menu_gets_no_launch_folder()
    {
        var client = await ConnectAsync(ClientRoles.Attach);

        await client.SendCommandAsync("menu");

        await ClipboardImageDaemonTests.Eventually(() => _panes.ByProgram("fleet") is not null);
        Assert.False(_panes.ByProgram("fleet")!.Env.ContainsKey(FleetDaemon.LaunchDirVariable));
    }
}
