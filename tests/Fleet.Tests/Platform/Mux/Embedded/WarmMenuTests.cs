using Fleet.Platform.Mux.Embedded.Daemon;
using Fleet.Platform.Mux.Embedded.Protocol;

namespace Fleet.Tests.Platform.Mux.Embedded;

public sealed class WarmMenuTests : IAsyncLifetime
{
    private readonly FakePanes _panes = new();
    private readonly List<DaemonTests.TestClient> _clients = [];
    private readonly CancellationTokenSource _stop = new();
    private Endpoint _endpoint = null!;
    private Task _running = Task.CompletedTask;

    public Task InitializeAsync()
    {
        var name = $"fleet-test-{Guid.NewGuid():N}"[..20];
        _endpoint = new Endpoint(OperatingSystem.IsWindows() ? name : Path.Combine(Path.GetTempPath(), name + ".sock"));
        var daemon = new FleetDaemon(new DaemonOptions
        {
            Endpoint = _endpoint,
            Pty = _panes.NewPty,
            Terminal = _panes.NewTerminal,
            FleetExecutable = "fleet",
            WarmMenus = true,
        });
        _running = daemon.RunAsync(_stop.Token);
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

    private async Task<DaemonTests.TestClient> ConnectAsync(string role, string? workspace = null)
    {
        var client = await DaemonTests.TestClient.ConnectAsync(_endpoint, role, 100, 30, workspace);
        _clients.Add(client);
        return client;
    }

    private int Menus => _panes.Started.Count(p => p.Program == "fleet");

    private async Task<(DaemonTests.TestClient Control, DaemonTests.TestClient Client, FakePanes.FakePty Warm)> WarmAsync()
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

        var client = await ConnectAsync(ClientRoles.Attach, "techweb");
        await client.WaitForFramesAsync(1);
        await Eventually(() => Menus == 1);

        var warm = _panes.ByProgram("fleet")!;
        Assert.Equal(["menu", "--project", "techweb"], warm.Args);
        Assert.True((await control.RequestAsync(new ControlRequest
        {
            Op = "fit",
            Caller = warm.Env[FleetDaemon.PaneVariable],
            Cols = 40,
            Rows = 8,
        })).Ok);
        warm.Emit("WARM-MENU\nitems\n");
        return (control, client, warm);
    }

    [Fact]
    public async Task A_warm_menu_is_ready_before_it_is_asked_for_and_shown_without_starting_a_process()
    {
        var (control, client, _) = await WarmAsync();
        await Task.Delay(300);

        Assert.DoesNotContain("WARM-MENU", client.AllText);
        var listed = (await control.RequestAsync(new ControlRequest { Op = "list-panes" })).Panes!;
        Assert.DoesNotContain(listed, p => p.Tab == "float");

        await client.SendCommandAsync("menu");
        await client.WaitForAsync("WARM-MENU");
        Assert.Contains("fleet menu", client.AllText);
        Assert.Equal(1, Menus);

        await client.SendKeyAsync("s");
        await Eventually(() => _panes.ByProgram("fleet")!.Written == "s");
    }

    [Fact]
    public async Task The_next_warm_menu_starts_after_the_open_one_closes()
    {
        var (_, client, warm) = await WarmAsync();
        await client.SendCommandAsync("menu");
        await client.WaitForAsync("WARM-MENU");

        await Task.Delay(300);
        Assert.Equal(1, Menus);

        warm.Exit();
        await Eventually(() => Menus == 2);
    }

    [Fact]
    public async Task A_warm_menu_switches_the_client_it_was_opened_for()
    {
        var (control, client, warm) = await WarmAsync();
        Assert.True((await control.RequestAsync(new ControlRequest
        {
            Op = "spawn",
            Session = "other",
            NewWindow = true,
            Cwd = ".",
            Args = ["other-shell"],
        })).Ok);
        _panes.ByProgram("other-shell")!.Emit("OTHER-SCREEN");

        await client.SendCommandAsync("menu");
        await client.WaitForAsync("WARM-MENU");

        var shown = await control.RequestAsync(new ControlRequest
        {
            Op = "show",
            Caller = warm.Env[FleetDaemon.PaneVariable],
            Workspace = "other",
        });

        Assert.True(shown.Ok, shown.Error);
        await client.WaitForAsync("OTHER-SCREEN");
    }

    [Fact]
    public async Task A_warm_menu_does_not_keep_an_emptied_workspace_alive()
    {
        var (control, _, _) = await WarmAsync();

        _panes.ByProgram("claude")!.Exit();

        await Eventually(() => _panes.Started.Where(p => p.Program == "fleet").All(p => p.IsDisposed));
        var workspaces = (await control.RequestAsync(new ControlRequest { Op = "list-workspaces" })).Workspaces!;
        Assert.DoesNotContain(workspaces, w => w.Name == "techweb");
    }

    private static async Task Eventually(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline && !condition())
        {
            await Task.Delay(20);
        }

        Assert.True(condition());
    }
}
