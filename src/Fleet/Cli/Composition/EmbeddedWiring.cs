using System.Diagnostics;
using System.Text.Json;
using Fleet.Features.Diagnostics.RunDoctor.Models;
using Fleet.Platform.Forwards;
using Fleet.Platform.Mux.Embedded;
using Fleet.Platform.Mux.Embedded.Client;
using Fleet.Platform.Mux.Embedded.Daemon;
using Fleet.Platform.Mux.Embedded.Host;
using Fleet.Platform.Mux.Embedded.Input;
using Fleet.Platform.Mux.Embedded.Model;
using Fleet.Platform.Mux.Embedded.Native;
using Fleet.Platform.Mux.Embedded.Protocol;
using Fleet.Platform.Mux.Embedded.Pty;
using Fleet.Platform.Mux.Embedded.Render;
using Fleet.Platform.Profiles;
using Fleet.Platform.Storage;
using Fleet.Ports;
using Fleet.Ports.Mux;
using Fleet.Ports.Mux.Models;
using Fleet.Ports.Projects.Models;
using Fleet.Shared;
using Fleet.Shared.Constants;
using Fleet.Shared.Iso;
using Fleet.Shared.Keymap;
using Fleet.Shared.Keymap.Enums;
using Fleet.Shared.Releases;
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

        Composer.Use(Adapters.Themes().Active());
        using var themeWatch = ThemeWiring.Follow(Composer.Use);

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
            Shell = () => DefaultShell.Resolve(
                Environment.GetEnvironmentVariable,
                Platform.Mux.Models.MuxEnvironment.OnPath,
                DefaultShell.WindowsTerminalSettings,
                OperatingSystem.IsWindows()),
            RemoteOpen = (host, token) => RemoteChannelOver(RemoteSsh(host, token, Endpoint.Default())),
            Notices = LocalNotices,
            SavedProjects = () => [.. Adapters.Projects().List().Select(p => p.Name)],
            OpenProject = ProjectOpener.EnsureOpenAsync,
            DismissNotices = (project, keys) =>
            {
                var store = Adapters.Notices();
                store.Save(project, Features.Notifications.SyncNotices.NoticeSync.Dismiss(store.Load(project), keys, DateTime.UtcNow));
            },
            AlertSettings = () => Adapters.Notices().Settings() is var s ? (s.Bell, s.Toast) : (false, false),
            Toast = (title, body) => Platform.Notifications.DesktopToast.Show(title, body),
            Head = HeadWiring.ServeOrigin(Driver, log),
            Iso = new CachedIsoMode(Adapters.Iso()).Load,
            Worktrees = project => Adapters.Agents().List(project).Select(a => a.Worktree),
            Forwards = ForwardWiring(log),
            ProjectConfigs = ProjectConfigsCached,
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

    public static string[] AttachArgs(string project, string? sshHost) =>
        sshHost is null ? ["attach", "--project", project] : ["attach", "--ssh", sshHost, "--project", project];

    private static bool OpenAttachWindow(string project, string? sshHost, IFleetLog log)
    {
        var env = new[]
            {
                Endpoint.Variable,
                FleetHome.OverrideVariable,
                AccountProfiles.PinnedVariable,
                AccountProfiles.AutoVariable,
                AccountProfiles.ConfigDirVariable,
            }
            .Select(name => (Name: name, Value: Environment.GetEnvironmentVariable(name)))
            .Where(v => !string.IsNullOrEmpty(v.Value))
            .ToDictionary(v => v.Name, v => v.Value!);

        var plan = NewWindow.Plan(
            Adapters.Executable,
            AttachArgs(project, sshHost),
            env,
            Environment.GetEnvironmentVariable,
            Platform.Mux.Models.MuxEnvironment.OnPath,
            OperatingSystem.IsWindows());

        return plan is not null && NewWindow.Open(plan, line => log.Write($"attach: {line}"));
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

    public static bool SameBuild(DaemonStatusDto status, string? build = null) =>
        PathKey.Same(status.Executable, Adapters.Executable)
        && status.Build is { } running
        && VersionCompare.AreEqual(running, build ?? FleetVersion.Current);

    public static async Task<string?> StaleDaemonAsync(string installed)
    {
        try
        {
            using var probe = new EmbeddedDriver(Endpoint.Default());
            return await probe.StatusAsync().ConfigureAwait(false) is { } status && !SameBuild(status, installed)
                ? $"fleetd is still on {(status.Build is { } build ? $"v{build}" : "an older build")}; your panes keep running on it. "
                    + "Restart it when convenient: fleet daemon stop, then fleet attach (this closes every pane)."
                : null;
        }
        catch (Ports.Mux.Exceptions.MuxUnavailableException e) when (e.Message.Contains("unknown operation", StringComparison.Ordinal))
        {
            return "fleetd is still on an older build; your panes keep running on it. "
                + "Restart it when convenient: fleet daemon stop, then fleet attach (this closes every pane).";
        }
        catch (Ports.Mux.Exceptions.MuxUnavailableException)
        {
            return null;
        }
    }

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
                    SameBuild(status),
                    status.Workspaces,
                    status.Panes,
                    status.WarmMenus,
                    status.Clients,
                    status.Build);
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

    public static async Task<int> AttachAsync(
        string? workspace, string? sshHost, IFleetLog log, Ports.Sessions.Models.WindowSession? session = null)
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
        var code = await new AttachClient(
                stream, workspace, () => Keys(log), line => log.Write($"attach: {line}"), mouse,
                project => OpenAttachWindow(project, sshHost, log),
                (project, host) => OpenAttachWindow(project, host, log),
                session is null ? null : hello => Furnish(hello, session))
            .RunAsync()
            .ConfigureAwait(false);

        if (code != 0 && sshHost is not null)
        {
            await Console.Error.WriteLineAsync(
                $"fleet: check that 'ssh {sshHost}' logs in, and that 'fleet' is on the remote PATH or named by FLEET_REMOTE_COMMAND.")
                .ConfigureAwait(false);
        }

        return code;
    }

    public static async Task<int> StopDaemonAsync()
    {
        using var driver = new EmbeddedDriver(Endpoint.Default());
        int pid;

        try
        {
            pid = (await driver.StatusAsync().ConfigureAwait(false))?.Pid ?? 0;
        }
        catch (Ports.Mux.Exceptions.MuxUnavailableException)
        {
            await Console.Out.WriteLineAsync("fleetd is not running").ConfigureAwait(false);
            return 0;
        }

        using var asked = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try
        {
            await driver.ShutdownAsync(asked.Token).ConfigureAwait(false);
        }
        catch (Exception e) when (e is Ports.Mux.Exceptions.MuxUnavailableException or OperationCanceledException)
        {
        }

        if (pid > 0 && !Exited(pid, TimeSpan.FromSeconds(10)))
        {
            await Console.Error.WriteLineAsync($"fleet: fleetd (pid {pid}) is still running; stop it with Stop-Process -Id {pid}")
                .ConfigureAwait(false);
            return 1;
        }

        await Console.Out.WriteLineAsync($"fleetd (pid {pid}) stopped: its panes are closed and its projects forgotten; fleet attach or opening a project starts fresh")
            .ConfigureAwait(false);
        return 0;
    }

    private static bool Exited(int pid, TimeSpan within)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.WaitForExit(within);
        }
        catch (ArgumentException)
        {
            return true;
        }
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

    public static bool PublishOwnFloatButtons(IReadOnlyList<FloatBorderButton> buttons)
    {
        try
        {
            _ownFloat ??= new EmbeddedDriver(Endpoint.Default());
            _ownFloat.FloatButtonsAsync(
                    [.. buttons.Select(b => new FloatButtonDto
                    {
                        Edge = b.Bottom ? "bottom" : "top",
                        Align = b.Right ? "right" : "left",
                        Key = b.Key,
                        Label = b.Label,
                        Send = b.Send,
                        Tip = b.Tip.Length > 0 ? b.Tip : null,
                    })])
                .GetAwaiter()
                .GetResult();
            return true;
        }
        catch (Ports.Mux.Exceptions.MuxUnavailableException)
        {
            return false;
        }
    }

    private static void Furnish(Hello hello, Ports.Sessions.Models.WindowSession session)
    {
        hello.Window = [.. session.Projects.Select(p => new WindowEntryDto { Name = p.Name, Host = p.Host })];
        hello.Showing = session.Showing is { } showing ? new WindowEntryDto { Name = showing.Name, Host = showing.Host } : null;
    }

    public static IReadOnlyList<(string Name, string? Host, bool Shown)> CurrentWindow()
    {
        try
        {
            using var driver = new EmbeddedDriver(Endpoint.Default());
            return [.. driver.WindowAsync().GetAwaiter().GetResult().Select(e => (e.Name, e.Host, e.Shown))];
        }
        catch (Ports.Mux.Exceptions.MuxUnavailableException)
        {
            return [];
        }
    }

    private static IReadOnlyList<NoticeDto> LocalNotices()
    {
        var store = Adapters.Notices();
        return [.. store.Projects().SelectMany(store.Load).Select(n => new NoticeDto
        {
            Project = n.Project,
            Key = n.Key,
            Kind = n.Kind.ToString(),
            Worktree = n.Worktree,
            Agent = n.Agent,
            Message = n.Message,
            Since = n.Since,
            Resolved = n.Resolved,
            Dismissed = n.Dismissed,
        })];
    }

    public static IReadOnlyList<NoticeDto> RemoteNotices()
    {
        try
        {
            using var driver = new EmbeddedDriver(Endpoint.Default());
            return driver.RemoteNoticesAsync().GetAwaiter().GetResult();
        }
        catch (Ports.Mux.Exceptions.MuxUnavailableException)
        {
            return [];
        }
    }

    public static RemoteNoticeView? WindowRemoteNotices()
    {
        var names = Adapters.Remotes().ListAsync().GetAwaiter().GetResult()
            .ToDictionary(m => m.Host, m => m.Label, StringComparer.OrdinalIgnoreCase);
        var labels = CurrentWindow()
            .Where(e => e.Host is not null)
            .ToDictionary(
                e => RemoteNoticeView.Label(e.Name, names.GetValueOrDefault(e.Host!, e.Host!)),
                e => (e.Host!, e.Name),
                StringComparer.OrdinalIgnoreCase);

        return labels.Count == 0 ? null : new RemoteNoticeView(labels, RemoteNotices);
    }

    public static void DismissRemote(string host, string project, IReadOnlyList<string> keys)
    {
        try
        {
            using var driver = new EmbeddedDriver(Endpoint.Default());
            driver.DismissRemoteAsync(host, project, keys).GetAwaiter().GetResult();
        }
        catch (Ports.Mux.Exceptions.MuxUnavailableException)
        {
        }
    }

    public static bool HandBack(string action)
    {
        if (Environment.GetEnvironmentVariable(FleetDaemon.ClientVariable) is not { Length: > 0 }
            && Environment.GetEnvironmentVariable(FleetDaemon.PaneVariable) is not { Length: > 0 })
        {
            return false;
        }

        try
        {
            using var driver = new EmbeddedDriver(Endpoint.Default());
            return driver.HandBackAsync(action).GetAwaiter().GetResult();
        }
        catch (Ports.Mux.Exceptions.MuxUnavailableException)
        {
            return false;
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

    public static bool Notices(string project, int open, bool bell)
    {
        try
        {
            _ownFloat ??= new EmbeddedDriver(Endpoint.Default());
            return _ownFloat.NoticesAsync(project, open, bell).GetAwaiter().GetResult();
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
        MuxKeys.Load(
            KeysFile,
            Environment.GetEnvironmentVariable(PrefixVariable),
            line => log.Write(line),
            HeadKeys(new Keymap(Adapters.Keymaps().Load())));

    public static IReadOnlyDictionary<string, string> HeadKeys(Keymap keymap)
    {
        var keys = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (action, command) in new[]
                 {
                     (FleetAction.OpenHeadVoice, MuxModel.HeadVerb + " voice"),
                 })
        {
            var spec = keymap.TextFor(action).Trim().ToLowerInvariant();

            if (spec.Length > 0)
            {
                keys[spec] = command;
            }
        }

        return keys;
    }

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

            try
            {
                if (!await BridgedHello.ForwardAsync(
                        stdin,
                        local,
                        Environment.GetEnvironmentVariable(BridgedHello.SshConnectionVariable),
                        Environment.GetEnvironmentVariable(BridgedHello.SshClientVariable)).ConfigureAwait(false))
                {
                    return 0;
                }
            }
            catch (Exception e) when (e is IOException or InvalidDataException or JsonException)
            {
                await Console.Error.WriteLineAsync($"fleet: bridge: {e.Message}").ConfigureAwait(false);
                return 1;
            }

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

        foreach (var name in (string[])[BridgedHello.SshConnectionVariable, BridgedHello.SshClientVariable, "SSH_TTY"])
        {
            start.Environment.Remove(name);
        }

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

    public static ProcessStartInfo RemoteSsh(string host, string token, Endpoint home)
    {
        var start = new ProcessStartInfo("ssh")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        var socket = ControlSocket(host);
        if (socket is not null)
        {
            ControlPaths.Reclaim(socket, ControlPaths.Answers);
        }

        foreach (var arg in RemoteSshArguments(host, socket, Environment.GetEnvironmentVariable(RemoteCommandVariable) ?? "fleet"))
        {
            start.ArgumentList.Add(arg);
        }

        start.Environment["SSH_ASKPASS"] = Adapters.Executable;
        start.Environment["SSH_ASKPASS_REQUIRE"] = "force";
        start.Environment[CommandLine.AskPassVariable] = token;
        start.Environment[Endpoint.Variable] = home.Address;
        return start;
    }

    public static IReadOnlyList<string> RemoteSshArguments(string host, string? controlPath, string remoteFleet) =>
    [
        "-T",
        "-o", "ConnectTimeout=15",
        .. controlPath is null ? [] : SshControl.MasterOptions(controlPath),
        host,
        remoteFleet,
        "bridge",
    ];

    private static readonly Lazy<string?> ControlSockets = new(() =>
    {
        if (!ControlPaths.Supported)
        {
            return null;
        }

        try
        {
            return ControlPaths.Prepare(ControlPaths.Default());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    });

    public static string? ControlSocket(string host) =>
        ControlSockets.Value is { } directory && ControlPaths.For(directory, host) is var path && ControlPaths.Fits(path) ? path : null;

    private static ForwardOptions ForwardWiring(IFleetLog log)
    {
        if (ControlSockets.Value is { } directory)
        {
            foreach (var stale in ControlPaths.CleanStale(directory, ControlPaths.Answers))
            {
                log.Write($"fleetd: removed the stale ssh control socket {stale}");
            }
        }

        var browser = new SystemBrowser();
        return new ForwardOptions { ControlPath = ControlSocket, OpenBrowser = browser.Open };
    }

    private static (DateTime At, IReadOnlyList<ProjectConfigDto> Configs) _configs = (DateTime.MinValue, []);

    private static IReadOnlyList<ProjectConfigDto> ProjectConfigsCached()
    {
        var cached = _configs;
        if (DateTime.UtcNow - cached.At < TimeSpan.FromSeconds(5))
        {
            return cached.Configs;
        }

        IReadOnlyList<ProjectConfigDto> fresh = [.. Adapters.Projects().List().Select(ProjectConfig)];
        _configs = (DateTime.UtcNow, fresh);
        return fresh;
    }

    public static ProjectConfigDto ProjectConfig(Project project) =>
        new()
        {
            Name = project.Name,
            Root = project.Root,
            ForwardPorts = project.ForwardPorts is { Count: > 0 } ports ? [.. ports] : null,
            RunCommand = project.RunCommand,
            ReadyPort = project.ReadyPort,
            HealthPath = project.HealthPath,
        };

    private static RemoteChannel RemoteChannelOver(ProcessStartInfo start)
    {
        RefuseSshInIso();
        var process = Process.Start(start) ?? throw new IOException("could not start ssh");
        return new RemoteChannel(
            new DuplexStream(process.StandardOutput.BaseStream, process.StandardInput.BaseStream),
            process.StandardError,
            new KilledOnDispose(process));
    }

    private sealed class KilledOnDispose(Process process) : IDisposable
    {
        public void Dispose()
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
            }

            process.Dispose();
        }
    }

    public static async Task<int> AskPassAsync(string prompt)
    {
        var token = Environment.GetEnvironmentVariable(CommandLine.AskPassVariable) ?? string.Empty;
        using var driver = new EmbeddedDriver(Endpoint.Default());
        var waited = Stopwatch.StartNew();

        while (waited.Elapsed < AskPassPatience)
        {
            try
            {
                var (pending, answer) = await driver.AskPassAsync(token, prompt).ConfigureAwait(false);
                if (!pending)
                {
                    await Console.Out.WriteLineAsync(answer ?? string.Empty).ConfigureAwait(false);
                    return 0;
                }
            }
            catch (Ports.Mux.Exceptions.MuxUnavailableException)
            {
                return 1;
            }

            await Task.Delay(250).ConfigureAwait(false);
        }

        return 1;
    }

    private static readonly TimeSpan AskPassPatience = TimeSpan.FromMinutes(5);

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

        RefuseSshInIso();

        start.ArgumentList.Add("-T");
        start.ArgumentList.Add(host);
        start.ArgumentList.Add(remote);
        start.ArgumentList.Add("bridge");

        var process = Process.Start(start) ?? throw new IOException("could not start ssh");
        return new DuplexStream(process.StandardOutput.BaseStream, process.StandardInput.BaseStream, process);
    }

    private static void RefuseSshInIso()
    {
        if (IsoGuard.Outbound(Adapters.Iso().Load(), IsoGuard.Ssh) is { Succeeded: false, Error: { } refused })
        {
            throw new IOException(refused);
        }
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
