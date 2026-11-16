using Fleet.Platform.Mux.Embedded.Daemon;
using Fleet.Platform.Mux.Embedded.Protocol;
using static Fleet.Tests.Platform.Mux.Embedded.DaemonTests;

namespace Fleet.Tests.Platform.Mux.Embedded;

public sealed class ShownAnywhereDaemonTests : IAsyncLifetime
{
    private readonly FakePanes _panes = new();
    private readonly List<IAsyncDisposable> _clients = [];
    private readonly CancellationTokenSource _stop = new();
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

    [Fact]
    public async Task The_workspace_list_tells_a_caller_without_a_client_which_projects_someone_is_viewing()
    {
        var control = await ControlAsync();
        await SpawnAsync(control, "techweb");
        await SpawnAsync(control, "fleet");
        _clients.Add(await TestClient.ConnectAsync(_endpoint, ClientRoles.Attach, 60, 12, "techweb"));

        var listed = (await control.RequestAsync(new ControlRequest { Op = "list-workspaces" })).Workspaces!
            .ToDictionary(w => w.Name);

        Assert.True(listed["techweb"].ShownAnywhere);
        Assert.False(listed["fleet"].ShownAnywhere);
    }

    private async Task<TestClient> ControlAsync()
    {
        var control = await TestClient.ConnectAsync(_endpoint, ClientRoles.Control, 0, 0, null);
        _clients.Add(control);
        return control;
    }

    private static async Task SpawnAsync(TestClient control, string workspace)
    {
        var spawned = await control.RequestAsync(new ControlRequest
        {
            Op = "spawn",
            Session = workspace,
            NewWindow = true,
            Cwd = ".",
            Args = ["claude"],
        });

        Assert.True(spawned.Ok, spawned.Error);
    }
}
