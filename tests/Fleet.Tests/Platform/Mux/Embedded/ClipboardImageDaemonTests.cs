using System.Collections.Concurrent;
using Fleet.Features.Shim.ServeClipboard;
using Fleet.Platform.Mux.Embedded.Client;
using Fleet.Platform.Mux.Embedded.Daemon;
using Fleet.Platform.Mux.Embedded.Protocol;
using Fleet.Ports;

namespace Fleet.Tests.Platform.Mux.Embedded;

public sealed class ClipboardImageDaemonTests : IAsyncLifetime
{
    private static readonly byte[] Png = [0x89, (byte)'P', (byte)'N', (byte)'G', 13, 10, 26, 10, 42];

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
            ServesBridge = true,
            ClipboardImageLimit = new ClipboardImageLimit(Reads: 4, Per: TimeSpan.FromMinutes(1)),
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
    public async Task The_focused_pane_gets_the_png_on_the_clipboard_of_the_viewer_focusing_it()
    {
        var pane = await SpawnAsync("techweb", "claude");
        var clipboard = new FakeClipboardImageSource(Png);
        await ViewAsync("techweb", clipboard);

        var answer = await AskAsync(pane);

        Assert.True(answer.Ok, answer.Error);
        Assert.Equal(ClipboardImageResponse.Png, answer.Mime);
        Assert.Equal(Png, answer.Data);
        Assert.Equal(1, clipboard.Reads);
    }

    [Fact]
    public async Task An_empty_viewer_clipboard_answers_with_no_image()
    {
        var pane = await SpawnAsync("techweb", "claude");
        await ViewAsync("techweb", new FakeClipboardImageSource(null));

        var answer = await AskAsync(pane);

        Assert.True(answer.Ok, answer.Error);
        Assert.Null(answer.Data);
    }

    [Fact]
    public async Task A_pane_no_viewer_focuses_is_refused_without_reading_any_clipboard()
    {
        var shown = await SpawnAsync("techweb", "claude");
        var hidden = await SpawnAsync("fleet", "claude");
        var clipboard = new FakeClipboardImageSource(Png);
        await ViewAsync("techweb", clipboard);

        var answer = await AskAsync(hidden);

        Assert.False(answer.Ok);
        Assert.Null(answer.Data);
        Assert.Contains("not focused", answer.Error);
        Assert.Equal(0, clipboard.Reads);
        Assert.NotEqual(shown, hidden);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0123456789abcdef0123456789abcdef")]
    public async Task A_caller_without_the_pane_token_is_refused_even_for_the_focused_pane(string? token)
    {
        var pane = await SpawnAsync("techweb", "claude");
        var clipboard = new FakeClipboardImageSource(Png);
        await ViewAsync("techweb", clipboard);

        var answer = await (await ControlAsync()).RequestAsync(
            new ControlRequest { Op = FleetDaemon.ClipboardImageOp, Pane = pane, Token = token });

        Assert.False(answer.Ok);
        Assert.Null(answer.Data);
        Assert.Contains("token", answer.Error);
        Assert.Equal(0, clipboard.Reads);
    }

    [Fact]
    public async Task Another_panes_token_does_not_open_the_focused_pane()
    {
        var focused = await SpawnAsync("techweb", "claude");
        var other = await SpawnAsync("fleet", "agent");
        var clipboard = new FakeClipboardImageSource(Png);
        await ViewAsync("techweb", clipboard);

        var answer = await AskAsync(focused, TokenOf(other));

        Assert.False(answer.Ok);
        Assert.Equal(0, clipboard.Reads);
    }

    [Fact]
    public async Task Every_pane_gets_its_own_token()
    {
        var a = await SpawnAsync("techweb", "claude");
        var b = await SpawnAsync("fleet", "agent");

        Assert.NotEqual(TokenOf(a), TokenOf(b));
        Assert.True(TokenOf(a).Length >= 32);
    }

    [Fact]
    public async Task A_pane_asking_faster_than_the_limit_is_refused_until_the_window_passes()
    {
        var pane = await SpawnAsync("techweb", "claude");
        var clipboard = new FakeClipboardImageSource(Png);
        await ViewAsync("techweb", clipboard);

        for (var i = 0; i < 4; i++)
        {
            Assert.True((await AskAsync(pane)).Ok);
        }

        var tripped = await AskAsync(pane);

        Assert.False(tripped.Ok);
        Assert.Contains("too many", tripped.Error);
        Assert.Equal(4, clipboard.Reads);
    }

    [Fact]
    public async Task The_viewer_shows_a_notice_when_a_pane_reads_its_clipboard_image_but_not_for_a_probe()
    {
        var pane = await SpawnAsync("techweb", "claude");
        var viewer = await ViewAsync("techweb", new FakeClipboardImageSource(Png));

        await ShimAsync(pane, "wl-paste", "--list-types");
        Assert.Empty(viewer.Notices);
        await ShimAsync(pane, "wl-paste", "--type", "image/png");

        var notice = Assert.Single(viewer.Notices);
        Assert.Contains(pane, notice);
        Assert.Contains("clipboard image", notice);
    }

    [Fact]
    public async Task Without_any_viewer_the_pane_is_refused()
    {
        var pane = await SpawnAsync("techweb", "claude");

        var answer = await AskAsync(pane);

        Assert.False(answer.Ok);
        Assert.Null(answer.Data);
    }

    [Fact]
    public async Task A_bridged_caller_is_refused_because_only_local_panes_run_the_shim()
    {
        var pane = await SpawnAsync("techweb", "claude");
        var clipboard = new FakeClipboardImageSource(Png);
        await ViewAsync("techweb", clipboard);
        var bridged = await DaemonTests.TestClient.ConnectAsync(_endpoint, ClientRoles.Control, 0, 0, null, bridged: true);
        _clients.Add(bridged);

        var answer = await bridged.RequestAsync(new ControlRequest { Op = FleetDaemon.ClipboardImageOp, Pane = pane, Token = TokenOf(pane) });

        Assert.False(answer.Ok);
        Assert.Null(answer.Data);
        Assert.Equal(0, clipboard.Reads);
    }

    [Fact]
    public async Task An_image_over_the_limit_is_refused_with_a_clear_error()
    {
        var pane = await SpawnAsync("techweb", "claude");
        await ViewAsync("techweb", new FakeClipboardImageSource(new byte[ClipboardImageResponse.MaxBytes + 1]));

        var answer = await AskAsync(pane);

        Assert.False(answer.Ok);
        Assert.Null(answer.Data);
        Assert.Contains("too large", answer.Error);
    }

    [Fact]
    public async Task An_osc_52_clipboard_read_from_the_focused_pane_stays_refused()
    {
        var pane = await SpawnAsync("techweb", "claude");
        var clipboard = new FakeClipboardImageSource(Png);
        var viewer = await ViewAsync("techweb", clipboard);

        _panes.ByProgram("claude")!.Emit("\e]52;c;?\a\e]52;c;?\e\\");
        await Task.Delay(300);

        Assert.Empty(viewer.Asked);
        Assert.Equal(0, clipboard.Reads);
        Assert.Equal(string.Empty, _panes.ByProgram("claude")!.Written);
        Assert.NotEmpty(pane);
    }

    [Fact]
    public async Task End_to_end_the_xclip_shim_in_the_focused_pane_prints_the_viewer_png()
    {
        var pane = await SpawnAsync("techweb", "claude");
        await ViewAsync("techweb", new FakeClipboardImageSource(Png));

        var (code, stdout, stderr) = await ShimAsync(pane, "xclip", "-selection", "clipboard", "-t", "image/png", "-o");

        Assert.Equal(0, code);
        Assert.Equal(Png, stdout);
        Assert.Empty(stderr);
    }

    [Fact]
    public async Task End_to_end_the_wl_paste_shim_lists_png_when_the_viewer_has_an_image()
    {
        var pane = await SpawnAsync("techweb", "claude");
        await ViewAsync("techweb", new FakeClipboardImageSource(Png));

        var (code, stdout, _) = await ShimAsync(pane, "wl-paste", "--list-types");

        Assert.Equal(0, code);
        Assert.Equal("image/png\n"u8.ToArray(), stdout);
    }

    [Fact]
    public async Task End_to_end_a_types_probe_then_a_read_ships_the_png_once()
    {
        var pane = await SpawnAsync("techweb", "claude");
        var clipboard = new FakeClipboardImageSource(Png);
        var viewer = await ViewAsync("techweb", clipboard);

        var probed = await ShimAsync(pane, "xclip", "-selection", "clipboard", "-t", "TARGETS", "-o");
        var listed = await ShimAsync(pane, "wl-paste", "--list-types");
        var read = await ShimAsync(pane, "xclip", "-selection", "clipboard", "-t", "image/png", "-o");

        Assert.Equal("TARGETS\nimage/png\n"u8.ToArray(), probed.Stdout);
        Assert.Equal("image/png\n"u8.ToArray(), listed.Stdout);
        Assert.Equal(Png, read.Stdout);
        Assert.Equal(1, viewer.ShippedImages);
        Assert.Equal(1, clipboard.Reads);
        Assert.Equal(2, clipboard.Probes);
    }

    [Fact]
    public async Task End_to_end_a_types_probe_with_an_empty_viewer_clipboard_lists_nothing()
    {
        var pane = await SpawnAsync("techweb", "claude");
        await ViewAsync("techweb", new FakeClipboardImageSource(null));

        var (code, stdout, _) = await ShimAsync(pane, "wl-paste", "--list-types");

        Assert.Equal(1, code);
        Assert.Empty(stdout);
    }

    [Fact]
    public async Task End_to_end_a_pane_the_viewer_does_not_focus_gets_no_image()
    {
        await SpawnAsync("techweb", "claude");
        var hidden = await SpawnAsync("fleet", "claude");
        var clipboard = new FakeClipboardImageSource(Png);
        await ViewAsync("techweb", clipboard);

        var (code, stdout, stderr) = await ShimAsync(hidden, "xclip", "-selection", "clipboard", "-t", "image/png", "-o");

        Assert.Equal(1, code);
        Assert.Empty(stdout);
        Assert.Contains("not focused", stderr);
        Assert.Equal(0, clipboard.Reads);
    }

    [Fact]
    public async Task End_to_end_an_empty_viewer_clipboard_exits_like_xclip_without_an_image()
    {
        var pane = await SpawnAsync("techweb", "claude");
        await ViewAsync("techweb", new FakeClipboardImageSource(null));

        var (code, stdout, stderr) = await ShimAsync(pane, "xclip", "-selection", "clipboard", "-t", "image/png", "-o");

        Assert.Equal(1, code);
        Assert.Empty(stdout);
        Assert.Equal("Error: target image/png not available", stderr.TrimEnd());
    }

    [Fact]
    public async Task A_shim_outside_any_fleet_pane_gets_no_image()
    {
        var answer = await new PaneClipboardImageSource(_endpoint, null, null).ReadPngAsync();

        Assert.Null(answer.Png);
        Assert.NotNull(answer.Refused);
    }

    private async Task<(int Code, byte[] Stdout, string Stderr)> ShimAsync(string pane, string tool, params string[] args)
    {
        var stdout = new MemoryStream();
        var stderr = new StringWriter();
        var code = await ClipboardShim.RunAsync(ClipboardShim.Parse(tool, args), new PaneClipboardImageSource(_endpoint, pane, TokenOf(pane)), stdout, stderr);
        return (code, stdout.ToArray(), stderr.ToString());
    }

    private async Task<DaemonTests.TestClient> ControlAsync()
    {
        var control = await DaemonTests.TestClient.ConnectAsync(_endpoint, ClientRoles.Control, 0, 0, null);
        _clients.Add(control);
        return control;
    }

    private async Task<string> SpawnAsync(string workspace, string program)
    {
        var spawned = await (await ControlAsync()).RequestAsync(new ControlRequest
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

    private async Task<ControlResponse> AskAsync(string pane, string? token = null) =>
        await (await ControlAsync()).RequestAsync(new ControlRequest { Op = FleetDaemon.ClipboardImageOp, Pane = pane, Token = token ?? TokenOf(pane) });

    private string TokenOf(string pane) =>
        _panes.Started.Single(p => p.Env.GetValueOrDefault(FleetDaemon.PaneVariable) == pane).Env[FleetDaemon.PaneTokenVariable];

    private async Task<ClipboardViewer> ViewAsync(string workspace, IClipboardImageSource clipboard)
    {
        var viewer = await ClipboardViewer.ConnectAsync(_endpoint, workspace, clipboard);
        _clients.Add(viewer);
        await Eventually(() => viewer.Frames > 0);
        return viewer;
    }

    internal static async Task Eventually(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "condition not met within 5 s");
            await Task.Delay(10);
        }
    }

    internal sealed class FakeClipboardImageSource(byte[]? png) : IClipboardImageSource
    {
        private int _reads;
        private int _probes;

        public int Reads => _reads;

        public int Probes => _probes;

        public Task<ClipboardImage> ProbePngAsync(CancellationToken ct = default)
        {
            Interlocked.Increment(ref _probes);
            return Task.FromResult(png is { Length: > 0 } ? ClipboardImage.Available : ClipboardImage.None);
        }

        public Task<ClipboardImage> ReadPngAsync(CancellationToken ct = default)
        {
            Interlocked.Increment(ref _reads);
            return Task.FromResult(new ClipboardImage(png));
        }
    }

    internal sealed class ClipboardViewer : IAsyncDisposable
    {
        private readonly Wire _wire;
        private readonly IClipboardImageSource _clipboard;
        private readonly Task _reader;
        private int _frames;
        private int _shipped;

        private ClipboardViewer(Wire wire, string id, IClipboardImageSource clipboard)
        {
            _wire = wire;
            Id = id;
            _clipboard = clipboard;
            _reader = Task.Run(ReadAsync);
        }

        public string Id { get; }

        public int Frames => _frames;

        public int ShippedImages => _shipped;

        public ConcurrentQueue<ClipboardImageRequest> Asked { get; } = new();

        public ConcurrentQueue<string> Notices { get; } = new();

        public static async Task<ClipboardViewer> ConnectAsync(Endpoint endpoint, string workspace, IClipboardImageSource clipboard)
        {
            var wire = new Wire(await endpoint.ConnectAsync(TimeSpan.FromSeconds(5)));
            await wire.SendAsync(
                MessageType.Hello,
                new Hello { Version = Wire.Version, Role = ClientRoles.Attach, Cols = 60, Rows = 12, Workspace = workspace, Os = "test", Bridged = true },
                WireJsonContext.Default.Hello);
            var welcome = (await wire.ReceiveAsync())!.Value;
            Assert.Equal(MessageType.Welcome, welcome.Type);
            return new ClipboardViewer(wire, Wire.Read(welcome.Payload, WireJsonContext.Default.Welcome).Client, clipboard);
        }

        public Task SendCommandAsync(string name, string? arg = null) =>
            _wire.SendAsync(MessageType.Command, new CommandMessage { Name = name, Arg = arg }, WireJsonContext.Default.CommandMessage);

        public async ValueTask DisposeAsync()
        {
            _wire.Dispose();
            try
            {
                await _reader;
            }
            catch (Exception e) when (e is IOException or ObjectDisposedException or OperationCanceledException)
            {
            }
        }

        private async Task ReadAsync()
        {
            try
            {
                while (await _wire.ReceiveAsync() is { } message)
                {
                    if (message.Type == MessageType.Frame)
                    {
                        Interlocked.Increment(ref _frames);
                    }
                    else if (message.Type == MessageType.ClipboardImageRequest)
                    {
                        var request = Wire.Read(message.Payload, WireJsonContext.Default.ClipboardImageRequest);
                        Asked.Enqueue(request);
                        var answer = await ClipboardImageAnswer.ForAsync(request, _clipboard, Notices.Enqueue);
                        if (answer.Data is { Length: > 0 })
                        {
                            Interlocked.Increment(ref _shipped);
                        }

                        await _wire.SendAsync(MessageType.ClipboardImageResponse, answer, WireJsonContext.Default.ClipboardImageResponse);
                    }
                }
            }
            catch (Exception e) when (e is IOException or ObjectDisposedException or EndOfStreamException or OperationCanceledException)
            {
            }
        }
    }
}
