using System.Collections.Concurrent;
using Fleet.Platform.Mux.Embedded.Daemon;
using Fleet.Platform.Mux.Embedded.Protocol;
using Fleet.Tests.Platform.Mux.Embedded;
using static Fleet.Tests.Platform.Mux.Embedded.ClipboardImageDaemonTests;

namespace Fleet.Tests.Features.Shim.WindowsFallback;

public sealed class PasteImageChordTests : IAsyncLifetime
{
    private static readonly byte[] Png = [0x89, (byte)'P', (byte)'N', (byte)'G', 13, 10, 26, 10, 5];

    private readonly FakePanes _panes = new();
    private readonly List<IAsyncDisposable> _clients = [];
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentQueue<byte[]> _dropped = new();
    private Endpoint _endpoint = null!;
    private Task _running = Task.CompletedTask;
    private string? _typed = @"C:\Temp\fleet-clipboard\clipboard-1.png";

    public Task InitializeAsync()
    {
        var name = $"fleet-test-{Guid.NewGuid():N}"[..20];
        _endpoint = new Endpoint(OperatingSystem.IsWindows() ? name : Path.Combine(Path.GetTempPath(), name + ".sock"));
        _running = new FleetDaemon(new DaemonOptions
        {
            Endpoint = _endpoint,
            Pty = _panes.NewPty,
            Terminal = _panes.NewTerminal,
            DropClipboardImage = png =>
            {
                _dropped.Enqueue(png);
                return _typed;
            },
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
    public async Task The_chord_types_the_saved_image_path_into_the_focused_pane()
    {
        var pane = await SpawnAsync("techweb", "claude");
        var clipboard = new FakeClipboardImageSource(Png);
        var viewer = await ViewAsync("techweb", clipboard);

        await viewer.SendCommandAsync(FleetDaemon.PasteImageCommand);

        await Eventually(() => PtyOf(pane).Written.Contains(_typed!, StringComparison.Ordinal));
        Assert.Equal(Png, Assert.Single(_dropped));
        Assert.Equal(1, clipboard.Reads);
        Assert.Contains(viewer.Notices, notice => notice.Contains(pane, StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_chord_with_no_image_on_the_viewer_clipboard_types_nothing()
    {
        var pane = await SpawnAsync("techweb", "claude");
        var clipboard = new FakeClipboardImageSource(null);
        var viewer = await ViewAsync("techweb", clipboard);

        await viewer.SendCommandAsync(FleetDaemon.PasteImageCommand);

        await Eventually(() => clipboard.Reads == 1);
        await Task.Delay(100);
        Assert.Empty(_dropped);
        Assert.DoesNotContain("png", PtyOf(pane).Written, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_save_that_fails_types_nothing()
    {
        _typed = null;
        var pane = await SpawnAsync("techweb", "claude");
        var viewer = await ViewAsync("techweb", new FakeClipboardImageSource(Png));

        await viewer.SendCommandAsync(FleetDaemon.PasteImageCommand);

        await Eventually(() => !_dropped.IsEmpty);
        await Task.Delay(100);
        Assert.DoesNotContain("png", PtyOf(pane).Written, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_chord_asks_only_the_viewer_that_pressed_it()
    {
        await SpawnAsync("techweb", "claude");
        var pressing = new FakeClipboardImageSource(Png);
        var other = new FakeClipboardImageSource(Png);
        var viewer = await ViewAsync("techweb", pressing);
        await ViewAsync("techweb", other);

        await viewer.SendCommandAsync(FleetDaemon.PasteImageCommand);

        await Eventually(() => !_dropped.IsEmpty);
        Assert.Equal(1, pressing.Reads);
        Assert.Equal(0, other.Reads);
    }

    private FakePanes.FakePty PtyOf(string pane) =>
        _panes.Started.Single(p => p.Env.GetValueOrDefault(FleetDaemon.PaneVariable) == pane);

    private async Task<string> SpawnAsync(string workspace, string program)
    {
        var control = await DaemonTests.TestClient.ConnectAsync(_endpoint, ClientRoles.Control, 0, 0, null);
        _clients.Add(control);
        var spawned = await control.RequestAsync(new ControlRequest
        {
            Op = "spawn",
            Session = workspace,
            NewWindow = true,
            Cwd = ".",
            Args = [program],
        });

        Assert.True(spawned.Ok, spawned.Error);
        return spawned.Pane!;
    }

    private async Task<ClipboardViewer> ViewAsync(string workspace, FakeClipboardImageSource clipboard)
    {
        var viewer = await ClipboardViewer.ConnectAsync(_endpoint, workspace, clipboard);
        _clients.Add(viewer);
        await Eventually(() => viewer.Frames > 0);
        return viewer;
    }
}
