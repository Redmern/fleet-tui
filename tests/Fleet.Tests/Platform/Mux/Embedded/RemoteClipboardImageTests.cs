using Fleet.Platform.Mux.Embedded;
using Fleet.Platform.Mux.Embedded.Daemon;
using Fleet.Platform.Mux.Embedded.Protocol;
using static Fleet.Tests.Platform.Mux.Embedded.ClipboardImageDaemonTests;

namespace Fleet.Tests.Platform.Mux.Embedded;

public sealed class RemoteClipboardImageTests : IAsyncLifetime
{
    private static readonly byte[] Png = [0x89, (byte)'P', (byte)'N', (byte)'G', 13, 10, 26, 10, 99];

    private readonly CancellationTokenSource _stop = new();
    private readonly List<Task> _running = [];
    private readonly FakePanes _homePanes = new();
    private readonly FakePanes _farPanes = new();
    private readonly List<IAsyncDisposable> _clients = [];
    private Endpoint _home = null!;
    private Endpoint _far = null!;

    private static Endpoint NewEndpoint()
    {
        var name = $"fleet-test-{Guid.NewGuid():N}"[..20];
        return new Endpoint(OperatingSystem.IsWindows() ? name : Path.Combine(Path.GetTempPath(), name + ".sock"));
    }

    public Task InitializeAsync()
    {
        _home = NewEndpoint();
        _far = NewEndpoint();
        _running.Add(new FleetDaemon(new DaemonOptions { Endpoint = _far, Pty = _farPanes.NewPty, Terminal = _farPanes.NewTerminal })
            .RunAsync(_stop.Token));
        _running.Add(new FleetDaemon(new DaemonOptions
        {
            Endpoint = _home,
            Pty = _homePanes.NewPty,
            Terminal = _homePanes.NewTerminal,
            RemoteOpen = (_, _) =>
            {
                var stream = _far.ConnectAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
                return new RemoteChannel(stream, null, stream);
            },
        }).RunAsync(_stop.Token));
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        foreach (var client in _clients)
        {
            await client.DisposeAsync();
        }

        await _stop.CancelAsync();
        await Task.WhenAll(_running);
    }

    [Fact]
    public async Task A_remote_pane_shown_in_a_local_window_gets_the_png_of_that_window_through_the_link()
    {
        var (farPane, viewer, clipboard) = await ShowRemotePaneAsync();

        var answer = await AskAsync(farPane);

        Assert.True(answer.Ok, answer.Error);
        Assert.Equal(Png, answer.Data);
        Assert.Equal(1, clipboard.Reads);
        Assert.NotNull(viewer.Id);
    }

    [Fact]
    public async Task A_remote_pane_the_local_window_no_longer_shows_gets_no_image()
    {
        var (farPane, viewer, clipboard) = await ShowRemotePaneAsync();
        await viewer.SendCommandAsync("show", "local");
        await Task.Delay(200);

        var answer = await AskAsync(farPane);

        Assert.False(answer.Ok);
        Assert.Null(answer.Data);
        Assert.Equal(0, clipboard.Reads);
    }

    private async Task<(string FarPane, ClipboardViewer Viewer, FakeClipboardImageSource Clipboard)> ShowRemotePaneAsync()
    {
        var far = await ControlAsync(_far);
        var spawned = await far.RequestAsync(new ControlRequest { Op = "spawn", Workspace = "homelab", Cwd = ".", Args = ["remote-claude"] });
        Assert.True(spawned.Ok, spawned.Error);

        var home = await ControlAsync(_home);
        Assert.True((await home.RequestAsync(new ControlRequest { Op = "spawn", Workspace = "local", Cwd = ".", Args = ["shell"] })).Ok);
        var clipboard = new FakeClipboardImageSource(Png);
        var viewer = await ClipboardViewer.ConnectAsync(_home, "local", clipboard);
        _clients.Add(viewer);
        await Eventually(() => viewer.Frames > 0);

        Assert.True((await home.RequestAsync(new ControlRequest { Op = "remote-connect", Host = "red@far" })).Ok);
        await EventuallyAsync(async () => (await home.RequestAsync(new ControlRequest { Op = "list-remotes" })).Remotes?.SingleOrDefault() is { State: RemoteLink.Connected });
        var shown = await home.RequestAsync(new ControlRequest { Op = "show-remote", Client = viewer.Id, Host = "red@far", Workspace = "homelab" });
        Assert.True(shown.Ok, shown.Error);
        await EventuallyAsync(async () => (await far.RequestAsync(new ControlRequest { Op = "list-panes" })).Panes?.Any(p => p.Id == spawned.Pane && p.Active) == true);
        await Task.Delay(200);
        return (spawned.Pane!, viewer, clipboard);
    }

    private async Task<ControlResponse> AskAsync(string pane) =>
        await (await ControlAsync(_far)).RequestAsync(new ControlRequest { Op = FleetDaemon.ClipboardImageOp, Pane = pane });

    private async Task<DaemonTests.TestClient> ControlAsync(Endpoint endpoint)
    {
        var control = await DaemonTests.TestClient.ConnectAsync(endpoint, ClientRoles.Control, 0, 0, null);
        _clients.Add(control);
        return control;
    }

    private static async Task EventuallyAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!await condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "condition not met within 10 s");
            await Task.Delay(50);
        }
    }
}
