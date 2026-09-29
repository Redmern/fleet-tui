using System.Diagnostics;
using System.Text.Json;
using Fleet.Features.Diagnostics.RunDoctor.Models;
using Fleet.Platform.Mux.Embedded;
using Fleet.Platform.Mux.Embedded.Client;
using Fleet.Platform.Mux.Embedded.Daemon;
using Fleet.Platform.Mux.Embedded.Host;
using Fleet.Platform.Mux.Embedded.Input;
using Fleet.Platform.Mux.Embedded.Model;
using Fleet.Platform.Mux.Embedded.Native;
using Fleet.Platform.Mux.Embedded.Pty;
using Fleet.Platform.Profiles;
using Fleet.Platform.Storage;
using Fleet.Ports;
using Fleet.Ports.Mux;
using Fleet.Ports.Mux.Models;
using Fleet.Ports.Projects.Models;
using Fleet.Shared;
using Fleet.Shared.Constants;
using Fleet.Shared.Keymap;
using Fleet.Ui;
using Fleet.Ui.Models;

namespace Fleet.Cli.Composition;

public static class EmbeddedWiring
{
    public const string PrefixVariable = "FLEET_PREFIX";

    public const string RemoteCommandVariable = "FLEET_REMOTE_COMMAND";

    public const string MouseVariable = "FLEET_MOUSE";

    private static readonly TimeSpan IdleExit = TimeSpan.FromSeconds(10);

    public static bool Ready => GhosttyNative.Available();

    public static bool InsideClient =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(FleetDaemon.ClientVariable))
        || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(FleetDaemon.PaneVariable));

    public static IMuxDriver Driver() => new EmbeddedDriver(Endpoint.Default(), StartDaemonAsync);

    public static async Task<int> RunDaemonAsync(IFleetLog log)
    {
        if (!Ready)
        {
            await Console.Error.WriteLineAsync(
                "fleet: this build carries no libghostty-vt, so it cannot run the embedded multiplexer")
                .ConfigureAwait(false);
            return 1;
        }

        if (OperatingSystem.IsWindows() && WindowsConsole.ReleaseRedirectedStdHandles() > 0)
        {
            log.Write("fleetd: released redirected std handles so pane children use their pseudoconsole");
        }

        if (OperatingSystem.IsWindows())
        {
            log.Write($"fleetd: panes use the {ConPtyApi.Current.Name} ConPTY");
        }

        var daemon = new FleetDaemon(new DaemonOptions
        {
            Endpoint = Endpoint.Default(),
            Pty = IPanePty.Create,
            Terminal = GhosttyTerminal.Create,
            Log = line => log.Write($"fleetd: {line}"),
            FleetExecutable = Adapters.Executable,
            ExitWhenEmptyAfter = IdleExit,
            SessionFile = SessionFile(Environment.GetEnvironmentVariable(Endpoint.Variable)),
            WarmMenus = true,
            PaneEnv = cwd => PaneProfile.Env(cwd, Adapters.Projects().List(), Profiles()),
        });

        await daemon.RunAsync().ConfigureAwait(false);
        return 0;
    }

    private static (DateTime Stamp, AccountProfiles? Profiles) _profiles;

    public static AccountProfiles? Profiles()
    {
        var file = AccountProfiles.DefaultFile;
        if (!File.Exists(file))
        {
            return null;
        }

        var stamp = File.GetLastWriteTimeUtc(file);
        if (_profiles.Stamp != stamp)
        {
            _profiles = (stamp, AccountProfiles.Parse(File.ReadAllText(file)));
        }

        return _profiles.Profiles;
    }

    public static string ProfilesFile => AccountProfiles.DefaultFile;

    public static string? ClaudeProfileOf(Project project)
    {
        try
        {
            if (Profiles() is not { } profiles)
            {
                return null;
            }

            if (project.ClaudeProfile is { Length: > 0 } pinned)
            {
                return profiles.Named(pinned) is { } found ? $"{found.Name} (pinned)" : $"{pinned} (pinned, NOT in {ProfilesFile})";
            }

            return $"{profiles.ForFolder(project.Root)?.Name ?? "none"} (by folder)";
        }
        catch (Exception e) when (e is FormatException or IOException or UnauthorizedAccessException)
        {
            return $"cannot read {ProfilesFile}: {e.Message}";
        }
    }

    public static (string ByFolder, IReadOnlyList<(string Name, string Folder)> Profiles)? ClaudeProfiles(string root) =>
        Profiles() is { All.Count: > 0 } profiles
            ? (profiles.ForFolder(root)?.Name ?? "none",
                [.. profiles.All.Select(p => (p.Name, AccountProfiles.ConfigDir(p) ?? "Claude's default folder"))])
            : null;

    public static async Task<EmbeddedHealth> HealthAsync()
    {
        FleetdStatus? fleetd = null;
        var tooOld = false;
        try
        {
            using var probe = new EmbeddedDriver(Endpoint.Default());
            if (await probe.StatusAsync().ConfigureAwait(false) is { } status)
            {
                fleetd = new FleetdStatus(
                    status.Pid,
                    status.Executable,
                    PathKey.Same(status.Executable, Adapters.Executable),
                    status.Workspaces,
                    status.Panes,
                    status.WarmMenus,
                    status.Clients);
            }
        }
        catch (Ports.Mux.Exceptions.MuxUnavailableException e)
        {
            tooOld = e.Message.Contains("unknown operation", StringComparison.Ordinal);
        }

        var file = SessionFile(Environment.GetEnvironmentVariable(Endpoint.Variable));
        if (!File.Exists(file))
        {
            return new EmbeddedHealth(Ready, fleetd, null, FleetdTooOld: tooOld);
        }

        try
        {
            var snapshot = JsonSerializer.Deserialize(File.ReadAllText(file), SessionJsonContext.Default.SessionSnapshot)
                ?? throw new JsonException("the file is empty");

            var saved = new SavedSession(
                file,
                File.GetLastWriteTime(file),
                snapshot.Workspaces.Count,
                snapshot.Workspaces.Sum(w => w.Floats.Count + w.Tabs.Sum(t => Leaves(t.Root))));

            return new EmbeddedHealth(Ready, fleetd, saved, FleetdTooOld: tooOld);
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return new EmbeddedHealth(Ready, fleetd, null, $"{file}: {e.Message}", tooOld);
        }
    }

    private static int Leaves(LayoutSnapshot? node) =>
        node is null ? 0 : node.Pane is not null ? 1 : Leaves(node.First) + Leaves(node.Second);

    public static string SessionFile(string? endpoint)
    {
        var suffix = string.IsNullOrWhiteSpace(endpoint)
            ? string.Empty
            : "-" + new string(endpoint.Trim().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-').ToArray());

        return Path.Combine(FleetHome.Config, $"embedded-session{suffix}.json");
    }

    public static async Task<int> AttachAsync(string? workspace, string? sshHost, IFleetLog log)
    {
        Stream stream;

        if (sshHost is not null)
        {
            stream = Ssh(sshHost);
        }
        else
        {
            var endpoint = Endpoint.Default();
            var local = await TryConnectAsync(endpoint).ConfigureAwait(false);

            if (local is null && await StartDaemonAsync().ConfigureAwait(false))
            {
                for (var i = 0; i < 50 && local is null; i++)
                {
                    await Task.Delay(100).ConfigureAwait(false);
                    local = await TryConnectAsync(endpoint).ConfigureAwait(false);
                }
            }

            if (local is null)
            {
                await Console.Error.WriteLineAsync($"fleet: fleetd is not reachable at {endpoint.Address}")
                    .ConfigureAwait(false);
                return 1;
            }

            stream = local;

            if (workspace is null)
            {
                var (cancelled, chosen) = await ChooseSessionAsync(endpoint).ConfigureAwait(false);
                if (cancelled)
                {
                    await stream.DisposeAsync().ConfigureAwait(false);
                    return 0;
                }

                workspace = chosen;
            }
        }

        var mouse = !string.Equals(Environment.GetEnvironmentVariable(MouseVariable), "off", StringComparison.OrdinalIgnoreCase);
        var code = await new AttachClient(stream, workspace, () => Keys(log), line => log.Write($"attach: {line}"), mouse)
            .RunAsync()
            .ConfigureAwait(false);

        if (code != 0 && sshHost is not null)
        {
            await Console.Error.WriteLineAsync(
                $"fleet: check that 'ssh {sshHost}' logs in without any prompt (a key or agent, and a " +
                "known host key), and that 'fleet' is on the remote PATH or named by FLEET_REMOTE_COMMAND.")
                .ConfigureAwait(false);
        }

        return code;
    }

    public static async Task<int> CliAsync(IReadOnlyList<string> args)
    {
        if (args is not ["activate-pane-direction", var direction, ..])
        {
            await Console.Error.WriteLineAsync("fleet cli: only 'activate-pane-direction <Left|Right|Up|Down>' is supported")
                .ConfigureAwait(false);
            return 2;
        }

        using var driver = new EmbeddedDriver(Endpoint.Default());
        await driver.FocusFromAsync(direction).ConfigureAwait(false);
        return 0;
    }

    private static EmbeddedDriver? _ownFloat;

    public static (int Cols, int Rows)? FitOwnFloat(int cols, int rows)
    {
        try
        {
            _ownFloat ??= new EmbeddedDriver(Endpoint.Default());
            return _ownFloat.FitAsync(cols, rows).GetAwaiter().GetResult();
        }
        catch (Ports.Mux.Exceptions.MuxUnavailableException)
        {
            return null;
        }
    }

    public static void HoldOwnFloat()
    {
        try
        {
            _ownFloat ??= new EmbeddedDriver(Endpoint.Default());
            _ownFloat.HoldAsync().GetAwaiter().GetResult();
        }
        catch (Ports.Mux.Exceptions.MuxUnavailableException)
        {
        }
    }

    public static bool OpenMenu(string? action)
    {
        try
        {
            using var driver = new EmbeddedDriver(Endpoint.Default());
            driver.OpenMenuAsync(action).GetAwaiter().GetResult();
            return true;
        }
        catch (Ports.Mux.Exceptions.MuxUnavailableException)
        {
            return false;
        }
    }

    public static IReadOnlyList<(string Name, int Panes)> Sessions(
        IReadOnlyList<Workspace> workspaces, IReadOnlyList<Pane> panes) =>
        [
            .. workspaces
                .Where(w => !FleetWorkspaces.IsHidden(w.Name))
                .Select(w => (w.Name, panes.Count(p => string.Equals(p.SessionName, w.Name, StringComparison.OrdinalIgnoreCase))))
                .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase),
        ];

    private static async Task<(bool Cancelled, string? Chosen)> ChooseSessionAsync(Endpoint endpoint)
    {
        IReadOnlyList<(string Name, int Panes)> sessions;

        try
        {
            using var driver = new EmbeddedDriver(endpoint);
            sessions = Sessions(
                await driver.ListWorkspacesAsync().ConfigureAwait(false),
                await driver.ListPanesAsync().ConfigureAwait(false));
        }
        catch (Ports.Mux.Exceptions.MuxUnavailableException)
        {
            return (false, null);
        }

        if (sessions.Count <= 1)
        {
            return (false, sessions.Count == 1 ? sessions[0].Name : null);
        }

        using var app = FleetUi.Start();
        var index = FleetPicker.Choose(
            app,
            "Attach to",
            [.. sessions.Select(s => new PickerEntry(s.Name, s.Panes == 1 ? "1 pane" : $"{s.Panes} panes"))],
            new Keymap(Adapters.Keymaps().Load()));

        return index is { } chosen ? (false, sessions[chosen].Name) : (true, null);
    }

    public static string KeysFile => Path.Combine(FleetPaths.Config, MuxKeys.FileName);

    public static MuxKeys Keys(IFleetLog log) =>
        MuxKeys.Load(KeysFile, Environment.GetEnvironmentVariable(PrefixVariable), line => log.Write(line));

    public static async Task<int> BridgeAsync()
    {
        var endpoint = Endpoint.Default();
        var local = await TryConnectAsync(endpoint).ConfigureAwait(false);

        if (local is null && await StartDaemonAsync().ConfigureAwait(false))
        {
            for (var i = 0; i < 50 && local is null; i++)
            {
                await Task.Delay(100).ConfigureAwait(false);
                local = await TryConnectAsync(endpoint).ConfigureAwait(false);
            }
        }

        if (local is null)
        {
            await Console.Error.WriteLineAsync($"fleet: fleetd is not reachable at {endpoint.Address}")
                .ConfigureAwait(false);
            return 1;
        }

        await using (local.ConfigureAwait(false))
        {
            var stdin = Console.OpenStandardInput();
            var stdout = Console.OpenStandardOutput();

            var up = stdin.CopyToAsync(local);
            var down = local.CopyToAsync(stdout);
            await Task.WhenAny(up, down).ConfigureAwait(false);
        }

        return 0;
    }

    public static async Task<bool> StartDaemonAsync()
    {
        if (!Ready)
        {
            return false;
        }

        var start = OperatingSystem.IsWindows() || !Adapters.OnPath("setsid")
            ? new ProcessStartInfo(Adapters.Executable) { ArgumentList = { "daemon" } }
            : new ProcessStartInfo("setsid") { ArgumentList = { "-f", Adapters.Executable, "daemon" } };

        start.UseShellExecute = false;
        start.CreateNoWindow = true;

        if (!OperatingSystem.IsWindows())
        {
            start.RedirectStandardInput = true;
            start.RedirectStandardOutput = true;
            start.RedirectStandardError = true;
        }

        try
        {
            using var process = Process.Start(start);
            await Task.Yield();
            return process is not null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static Stream Ssh(string host)
    {
        var remote = Environment.GetEnvironmentVariable(RemoteCommandVariable) ?? "fleet";
        var start = new ProcessStartInfo("ssh")
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = false,
        };

        start.ArgumentList.Add("-T");
        start.ArgumentList.Add("-o");
        start.ArgumentList.Add("BatchMode=yes");
        start.ArgumentList.Add(host);
        start.ArgumentList.Add(remote);
        start.ArgumentList.Add("bridge");

        var process = Process.Start(start) ?? throw new IOException("could not start ssh");
        return new DuplexStream(process.StandardOutput.BaseStream, process.StandardInput.BaseStream, process);
    }

    private static async Task<Stream?> TryConnectAsync(Endpoint endpoint)
    {
        try
        {
            return await endpoint.ConnectAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
        }
        catch (Exception e) when (e is IOException or TimeoutException or System.Net.Sockets.SocketException
                                      or OperationCanceledException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
