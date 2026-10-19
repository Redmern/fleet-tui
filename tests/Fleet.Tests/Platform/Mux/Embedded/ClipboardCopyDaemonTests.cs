using System.Collections.Concurrent;
using System.Text;
using Fleet.Features.Shim.ServeClipboard;
using Fleet.Platform.Mux.Embedded.Client;
using Fleet.Platform.Mux.Embedded.Daemon;
using Fleet.Platform.Mux.Embedded.Native;
using Fleet.Platform.Mux.Embedded.Protocol;
using static Fleet.Tests.Platform.Mux.Embedded.DaemonTests;

namespace Fleet.Tests.Platform.Mux.Embedded;

public sealed class ClipboardCopyDaemonTests : IAsyncLifetime
{
    private readonly FakePanes _panes = new();
    private readonly List<IAsyncDisposable> _clients = [];
    private readonly ConcurrentQueue<string> _log = new();
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
            Log = _log.Enqueue,
            ClipboardImageLimit = new ClipboardImageLimit(Reads: 2, Per: TimeSpan.FromMinutes(1)),
            ClipboardCopyLimit = new ClipboardImageLimit(Reads: 3, Per: TimeSpan.FromMinutes(1)),
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
    public async Task A_copy_with_the_pane_token_lands_on_the_viewer_clipboard()
    {
        var pane = await SpawnAsync("techweb", "claude");
        var viewer = await ViewAsync("techweb");

        var answer = await CopyAsync(pane, "hello from the remote pane");

        Assert.True(answer.Ok, answer.Error);
        await ClipboardImageDaemonTests.Eventually(() =>
            viewer.Effects.Any(e => e.Kind == HostEffects.Clipboard && e.Value == "hello from the remote pane"));
        Assert.Contains(_log, line => line.Contains($"{pane} copied 26 chars", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0123456789abcdef0123456789abcdef")]
    public async Task A_copy_without_the_pane_token_is_refused(string? token)
    {
        var pane = await SpawnAsync("techweb", "claude");
        var viewer = await ViewAsync("techweb");

        var answer = await (await ControlAsync()).RequestAsync(
            new ControlRequest { Op = FleetDaemon.ClipboardCopyOp, Pane = pane, Token = token, Text = "secret" });

        Assert.False(answer.Ok);
        Assert.Contains("token", answer.Error);
        await Task.Delay(200);
        Assert.DoesNotContain(viewer.Effects, e => e.Kind == HostEffects.Clipboard);
        Assert.Contains(_log, line => line.Contains($"clipboard copy from {pane} refused", StringComparison.Ordinal));
        Assert.DoesNotContain(_log, line => line.Contains("clipboard image", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_bridged_caller_cannot_copy()
    {
        var pane = await SpawnAsync("techweb", "claude");
        var viewer = await ViewAsync("techweb");
        var bridged = await TestClient.ConnectAsync(_endpoint, ClientRoles.Control, 0, 0, null, bridged: true);
        _clients.Add(bridged);

        var answer = await bridged.RequestAsync(
            new ControlRequest { Op = FleetDaemon.ClipboardCopyOp, Pane = pane, Token = TokenOf(pane), Text = "x" });

        Assert.False(answer.Ok);
        await Task.Delay(200);
        Assert.DoesNotContain(viewer.Effects, e => e.Kind == HostEffects.Clipboard);
    }

    [Fact]
    public async Task A_copy_over_the_osc_52_limit_is_refused_and_logged()
    {
        var pane = await SpawnAsync("techweb", "claude");
        var viewer = await ViewAsync("techweb");

        var answer = await CopyAsync(pane, new string('a', GhosttyTerminal.MaxClipboard + 1));

        Assert.False(answer.Ok);
        Assert.Contains("too large", answer.Error);
        Assert.Contains(_log, line => line.Contains($"clipboard copy from {pane} refused", StringComparison.Ordinal));
        await Task.Delay(200);
        Assert.DoesNotContain(viewer.Effects, e => e.Kind == HostEffects.Clipboard);
    }

    [Fact]
    public async Task A_copy_at_the_osc_52_limit_is_taken()
    {
        var pane = await SpawnAsync("techweb", "claude");
        await ViewAsync("techweb");

        var answer = await CopyAsync(pane, new string('a', GhosttyTerminal.MaxClipboard));

        Assert.True(answer.Ok, answer.Error);
    }

    [Fact]
    public async Task A_copy_without_text_is_refused()
    {
        var pane = await SpawnAsync("techweb", "claude");
        await ViewAsync("techweb");

        var answer = await (await ControlAsync()).RequestAsync(
            new ControlRequest { Op = FleetDaemon.ClipboardCopyOp, Pane = pane, Token = TokenOf(pane) });

        Assert.False(answer.Ok);
    }

    [Fact]
    public async Task A_copy_with_no_viewer_attached_is_refused_and_logged()
    {
        var pane = await SpawnAsync("techweb", "claude");

        var answer = await CopyAsync(pane, "nobody");

        Assert.False(answer.Ok);
        Assert.Contains("no viewer", answer.Error);
        Assert.Contains(_log, line => line.Contains("dropped", StringComparison.Ordinal) && line.Contains(pane, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Copies_have_their_own_rate_window_apart_from_image_reads()
    {
        var pane = await SpawnAsync("techweb", "claude");
        var control = await ControlAsync();
        for (var i = 0; i < 3; i++)
        {
            await control.RequestAsync(new ControlRequest { Op = FleetDaemon.ClipboardTypesOp, Pane = pane, Token = TokenOf(pane) });
        }

        Assert.Contains(_log, line => line.Contains($"clipboard image for {pane} refused: more than 2", StringComparison.Ordinal));
        await ViewAsync("techweb");

        for (var i = 0; i < 3; i++)
        {
            var copied = await CopyAsync(pane, $"copy {i}");
            Assert.True(copied.Ok, copied.Error);
        }

        var tripped = await CopyAsync(pane, "one too many");

        Assert.False(tripped.Ok);
        Assert.Contains("too many", tripped.Error);
        Assert.Contains(_log, line => line.Contains($"clipboard copy from {pane} refused: more than 3", StringComparison.Ordinal));
    }

    [Fact]
    public async Task End_to_end_wl_copy_in_the_pane_puts_its_stdin_on_the_viewer_clipboard()
    {
        var pane = await SpawnAsync("techweb", "claude");
        var viewer = await ViewAsync("techweb");

        var (code, stderr) = await WlCopyAsync(pane, TokenOf(pane), "yanked\n", "-n");

        Assert.Equal(0, code);
        Assert.Empty(stderr);
        await ClipboardImageDaemonTests.Eventually(() =>
            viewer.Effects.Any(e => e.Kind == HostEffects.Clipboard && e.Value == "yanked"));
    }

    [Fact]
    public async Task End_to_end_wl_copy_with_a_wrong_token_fails_with_the_reason()
    {
        var pane = await SpawnAsync("techweb", "claude");
        await ViewAsync("techweb");

        var (code, stderr) = await WlCopyAsync(pane, "not-the-token", "text");

        Assert.Equal(1, code);
        Assert.Contains("token", stderr);
    }

    [Fact]
    public async Task End_to_end_wl_copy_outside_any_fleet_pane_fails()
    {
        var copied = await new PaneClipboardImageSource(_endpoint, null, null).CopyAsync("text");

        Assert.NotNull(copied.Refused);
    }

    private async Task<(int Code, string Stderr)> WlCopyAsync(string pane, string token, string stdin, params string[] args)
    {
        var stderr = new StringWriter();
        var code = await ClipboardShim.CopyAsync(
            ClipboardShim.Parse(ClipboardShim.WlCopy, args),
            new MemoryStream(Encoding.UTF8.GetBytes(stdin)),
            new PaneClipboardImageSource(_endpoint, pane, token),
            stderr);
        return (code, stderr.ToString());
    }

    private async Task<ControlResponse> CopyAsync(string pane, string text) =>
        await (await ControlAsync()).RequestAsync(
            new ControlRequest { Op = FleetDaemon.ClipboardCopyOp, Pane = pane, Token = TokenOf(pane), Text = text });

    private async Task<TestClient> ControlAsync()
    {
        var control = await TestClient.ConnectAsync(_endpoint, ClientRoles.Control, 0, 0, null);
        _clients.Add(control);
        return control;
    }

    private async Task<TestClient> ViewAsync(string workspace)
    {
        var viewer = await TestClient.ConnectAsync(_endpoint, ClientRoles.Attach, 60, 12, workspace, bridged: true);
        _clients.Add(viewer);
        await ClipboardImageDaemonTests.Eventually(() => viewer.FrameCount > 0);
        return viewer;
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

    private string TokenOf(string pane) =>
        _panes.Started.Single(p => p.Env.GetValueOrDefault(FleetDaemon.PaneVariable) == pane).Env[FleetDaemon.PaneTokenVariable];
}
