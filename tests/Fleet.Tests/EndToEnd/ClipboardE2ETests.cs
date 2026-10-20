using System.Diagnostics;
using System.Text.Json;
using Fleet.Cli.Composition;
using Fleet.Platform.Clipboard;
using Fleet.Platform.Mux.Embedded.Daemon;
using Fleet.Platform.Mux.Embedded.Protocol;
using Fleet.Shared;
using Fleet.Shared.Constants;
using Fleet.Tests.Platform.Mux.Embedded;

namespace Fleet.Tests.EndToEnd;

// Black-box: two real fleetd processes (the built `fleet` apphost, real libghostty, real ptys), the remote reached
// through FLEET_BRIDGE_COMMAND instead of ssh, a real `fleet attach` in a pty, and real nvim panes. A yank must land in
// the viewer's FLEET_CLIPBOARD_SINK. The far side has no DISPLAY or WAYLAND_DISPLAY, like a headless remote.
[Trait("Category", "e2e")]
public sealed class ClipboardE2ETests : IAsyncLifetime
{
    private const string Host = "e2e-far";

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"fe2e-{Guid.NewGuid():N}"[..13]);
    private readonly List<Process> _processes = [];
    private readonly List<IAsyncDisposable> _clients = [];
    private Endpoint _far = null!;
    private Endpoint _home = null!;

    private const string BinaryVariable = "FLEET_E2E_FLEET";

    private const string GhosttyDirVariable = "FLEET_E2E_GHOSTTY_DIR";

    private static string? Given => Environment.GetEnvironmentVariable(BinaryVariable) is { Length: > 0 } given ? given : null;

    private static string Fleet => Given ?? Path.Combine(AppContext.BaseDirectory, "fleet");

    private static readonly Lazy<string?> GhosttyDir = new(FindGhostty);

    private string Sink => Path.Combine(_root, "sink.ndjson");

    private string Trace => Path.Combine(_root, "trace.ndjson");

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(_root);
        _far = new Endpoint(Path.Combine(_root, "far.sock"));
        _home = new Endpoint(Path.Combine(_root, "home.sock"));
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        foreach (var process in _processes)
        {
            try
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token);
            }
            catch (Exception e) when (e is InvalidOperationException or OperationCanceledException or System.ComponentModel.Win32Exception)
            {
            }

            process.Dispose();
        }

        foreach (var client in _clients)
        {
            try
            {
                await client.DisposeAsync();
            }
            catch (Exception e) when (e is IOException or ObjectDisposedException or OperationCanceledException)
            {
            }
        }

        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    [E2EFact]
    public async Task A_yank_in_unmarked_and_mcp_spawned_remote_nvim_panes_reaches_the_viewer_sink()
    {
        StartDaemon("far", _far, remote: true);
        var far = await ControlAsync(_far);
        var before = await SpawnNvimAsync(far, "before", "marker-before-attach");

        StartDaemon("home", _home, remote: false);
        var home = await ControlAsync(_home);
        Assert.True((await home.RequestAsync(new ControlRequest { Op = "spawn", Workspace = "local", Cwd = _root, Args = ["sh"] })).Ok);
        StartViewer();
        await Eventually(async () => (await home.RequestAsync(new ControlRequest { Op = "status" })).Status?.Clients >= 1, "the viewer attaches");

        Assert.True((await home.RequestAsync(new ControlRequest { Op = "remote-connect", Host = Host })).Ok);
        await Eventually(
            async () => (await home.RequestAsync(new ControlRequest { Op = "list-remotes" })).Remotes?.SingleOrDefault() is { State: RemoteLink.Connected },
            "the bridge connects");
        var shown = await home.RequestAsync(new ControlRequest { Op = "show-remote", Client = "c1", Host = Host, Workspace = "before" });
        Assert.True(shown.Ok, shown.Error);

        await YankAsync(far, before, "marker-before-attach");
        Assert.False(EnvOf(before).ContainsKey(AgentHarness.RemotePaneVariable));

        var dispatched = await SpawnNvimAsync(far, "dispatched", "marker-mcp-spawned");
        await YankAsync(far, dispatched, "marker-mcp-spawned");
        Assert.Equal("1", EnvOf(dispatched)[AgentHarness.RemotePaneVariable]);
    }

    private async Task YankAsync(DaemonTests.TestClient far, string pane, string marker)
    {
        Assert.True((await far.RequestAsync(new ControlRequest { Op = "send-text", Pane = pane, Text = "Vy" })).Ok);
        await Eventually(
            () => Task.FromResult(SinkTexts().Contains(marker + "\n")),
            $"the yank of {marker} reaches the sink",
            TimeSpan.FromSeconds(10));
    }

    // The viewer may be halfway through appending a line, so one that does not parse yet is skipped, not fatal.
    private List<string> SinkTexts()
    {
        if (!File.Exists(Sink))
        {
            return [];
        }

        List<string> texts = [];
        foreach (var line in File.ReadAllLines(Sink))
        {
            try
            {
                texts.Add(JsonDocument.Parse(line).RootElement.GetProperty("text").GetString() ?? string.Empty);
            }
            catch (JsonException)
            {
            }
        }

        return texts;
    }

    private Dictionary<string, string> EnvOf(string pane)
    {
        var pid = File.ReadAllText(Path.Combine(_root, $"{pane}.pid")).Trim();
        return File.ReadAllText($"/proc/{pid}/environ").Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Select(e => e.Split('=', 2))
            .Where(kv => kv.Length == 2)
            .GroupBy(kv => kv[0])
            .ToDictionary(g => g.Key, g => g.Last()[1]);
    }

    private async Task<string> SpawnNvimAsync(DaemonTests.TestClient far, string workspace, string marker)
    {
        var dir = Directory.CreateDirectory(Path.Combine(_root, workspace)).FullName;
        await File.WriteAllTextAsync(Path.Combine(dir, "f.txt"), marker + "\n");
        await File.WriteAllTextAsync(Path.Combine(dir, "init.lua"), "vim.o.clipboard='unnamedplus' vim.o.swapfile=false");
        await File.WriteAllTextAsync(Path.Combine(dir, "boot.lua"), AgentHarness.RemoteClipboard);
        var spawned = await far.RequestAsync(new ControlRequest
        {
            Op = "spawn",
            Workspace = workspace,
            NewWindow = true,
            Cwd = dir,
            Args = ["sh", "-c", $"echo $$ > '{_root}/'\"$FLEET_PANE\".pid; exec nvim --clean -u init.lua -c 'luafile boot.lua' f.txt"],
        });
        Assert.True(spawned.Ok, spawned.Error);
        var pane = spawned.Pane!;
        await Eventually(
            async () => (await far.RequestAsync(new ControlRequest { Op = "get-text", Pane = pane })).Text?.Contains(marker, StringComparison.Ordinal) == true
                && File.Exists(Path.Combine(_root, $"{pane}.pid")),
            $"nvim shows {marker}");
        await Task.Delay(300);
        return pane;
    }

    private Dictionary<string, string> SideEnv(string side, Endpoint endpoint)
    {
        var home = Directory.CreateDirectory(Path.Combine(_root, side)).FullName;
        var runtime = Directory.CreateDirectory(Path.Combine(home, "run")).FullName;
        return new Dictionary<string, string>
        {
            ["HOME"] = home,
            ["XDG_CONFIG_HOME"] = Path.Combine(home, ".config"),
            ["XDG_DATA_HOME"] = Path.Combine(home, ".local", "share"),
            ["XDG_STATE_HOME"] = Path.Combine(home, ".local", "state"),
            ["XDG_RUNTIME_DIR"] = runtime,
            [FleetHome.OverrideVariable] = Path.Combine(home, "fleet"),
            [Endpoint.Variable] = endpoint.Address,
            [AgentHarness.ClipboardTraceVariable] = Trace,
            ["TERM"] = "xterm-256color",
            ["LD_LIBRARY_PATH"] = string.Join(
                Path.PathSeparator,
                new[] { Given is null ? GhosttyDir.Value : null, Environment.GetEnvironmentVariable("LD_LIBRARY_PATH") }
                    .Where(d => !string.IsNullOrEmpty(d))),
        };
    }

    // A test build runs fleet on the JIT, so it loads libghostty-vt.so at run time: the one scripts/ghostty/build.sh
    // leaves in artifacts/, one named by FLEET_E2E_GHOSTTY_DIR, or a system one with the pinned API.
    private static string? FindGhostty()
    {
        var candidates = new List<string?> { Environment.GetEnvironmentVariable(GhosttyDirVariable) };
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            candidates.Add(Path.Combine(dir.FullName, "artifacts", "ghostty", "linux-x64", "lib"));
        }

        candidates.AddRange(["/usr/local/lib", "/usr/lib", "/usr/lib64", "/usr/lib/x86_64-linux-gnu"]);
        return candidates.OfType<string>().FirstOrDefault(dir =>
            File.Exists(Path.Combine(dir, "libghostty-vt.so"))
            && System.Runtime.InteropServices.NativeLibrary.TryLoad(Path.Combine(dir, "libghostty-vt.so"), out var lib)
            && System.Runtime.InteropServices.NativeLibrary.TryGetExport(lib, "ghostty_type_json", out _));
    }

    private void StartDaemon(string side, Endpoint endpoint, bool remote)
    {
        var start = Start(Fleet, ["daemon"], SideEnv(side, endpoint));
        if (remote)
        {
            start.Environment[ShimFolder.RemoteDaemonVariable] = "1";
        }
        else
        {
            var far = string.Join(' ', SideEnv("far", _far).Select(kv => $"{kv.Key}='{kv.Value}'"));
            start.Environment[EmbeddedWiring.BridgeCommandVariable] = $"exec env {far} '{Fleet}' bridge";
        }

        Run(start);
    }

    private void StartViewer()
    {
        var env = SideEnv("home", _home);
        env[ClipboardSink.Variable] = Sink;
        Run(Start("script", ["-q", "-e", "-c", $"stty cols 120 rows 40; exec '{Fleet}' attach --project local", "/dev/null"], env));
    }

    private static ProcessStartInfo Start(string file, IReadOnlyList<string> args, Dictionary<string, string> env)
    {
        var start = new ProcessStartInfo(file)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        foreach (var name in (string[])["DISPLAY", "WAYLAND_DISPLAY", "TMUX", "SSH_CONNECTION", "SSH_CLIENT", "SSH_TTY",
                     ClipboardSink.Variable, EmbeddedWiring.BridgeCommandVariable, AgentHarness.RemotePaneVariable,
                     FleetDaemon.PaneVariable, FleetDaemon.ClientVariable])
        {
            start.Environment.Remove(name);
        }

        foreach (var (key, value) in env)
        {
            start.Environment[key] = value;
        }

        return start;
    }

    private void Run(ProcessStartInfo start)
    {
        var process = Process.Start(start)!;
        _processes.Add(process);
        _ = process.StandardOutput.BaseStream.CopyToAsync(Stream.Null);
        _ = process.StandardError.BaseStream.CopyToAsync(Stream.Null);
    }

    private async Task<DaemonTests.TestClient> ControlAsync(Endpoint endpoint)
    {
        DaemonTests.TestClient? client = null;
        await Eventually(
            async () =>
            {
                try
                {
                    client = await DaemonTests.TestClient.ConnectAsync(endpoint, ClientRoles.Control, 0, 0, null);
                    return true;
                }
                catch (Exception e) when (e is IOException or System.Net.Sockets.SocketException or TimeoutException)
                {
                    return false;
                }
            },
            $"fleetd listens on {endpoint.Address}");
        _clients.Add(client!);
        return client!;
    }

    private async Task Eventually(Func<Task<bool>> condition, string what, TimeSpan? within = null)
    {
        var deadline = DateTime.UtcNow + (within ?? TimeSpan.FromSeconds(20));
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(100);
        }

        Assert.Fail($"timed out waiting until {what}; sink: [{string.Join(" | ", SinkTexts())}]; trace:\n"
            + (File.Exists(Trace) ? await File.ReadAllTextAsync(Trace) : "(none)")
            + "\nlogs:\n" + Logs());
    }

    private string Logs() =>
        string.Join('\n', Directory.EnumerateFiles(_root, "*.log", SearchOption.AllDirectories)
            .Select(f => $"== {f}\n{string.Join('\n', File.ReadLines(f).TakeLast(40))}"));

    private sealed class E2EFactAttribute : FactAttribute
    {
        public E2EFactAttribute()
        {
            List<string> missing =
            [
                .. OperatingSystem.IsLinux() ? [] : (string[])["Linux"],
                .. OnPath("nvim") ? [] : (string[])["nvim"],
                .. OnPath("script") ? [] : (string[])["util-linux script"],
                .. File.Exists(Fleet) ? [] : (string[])["the built fleet apphost"],
                .. Given is not null || (OperatingSystem.IsLinux() && GhosttyDir.Value is not null)
                    ? []
                    : (string[])[$"libghostty-vt.so with ghostty_type_json (scripts/ghostty/build.sh, {GhosttyDirVariable}) or {BinaryVariable}"],
            ];
            if (missing.Count > 0)
            {
                Skip = $"needs {string.Join(", ", missing)}";
            }
        }

        private static bool OnPath(string tool) =>
            (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Any(dir => File.Exists(Path.Combine(dir, tool)));
    }
}
