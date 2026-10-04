using Fleet.Features.Projects.SwitchProject;
using Fleet.Platform.Mux.Embedded;
using Fleet.Platform.Mux.Embedded.Daemon;
using Fleet.Platform.Mux.Embedded.Protocol;
using Fleet.Ports.Remotes.Enums;
using Fleet.Ports.Remotes.Models;
using Fleet.Ui.Models;

namespace Fleet.Tests.Platform.Mux.Embedded;

public sealed class RemoteLinkTests : IAsyncLifetime
{
    private readonly CancellationTokenSource _stop = new();
    private readonly List<Task> _running = [];
    private readonly FakePanes _homePanes = new();
    private readonly FakePanes _farPanes = new();
    private readonly List<DaemonTests.TestClient> _clients = [];
    private Endpoint _home = null!;
    private Endpoint _far = null!;
    private Func<string, string, RemoteChannel>? _open;
    private volatile List<NoticeDto> _farNotices = [];
    private readonly System.Collections.Concurrent.ConcurrentQueue<(string Project, IReadOnlyList<string> Keys)> _farDismissed = new();
    private readonly System.Collections.Concurrent.ConcurrentQueue<string> _toasts = new();
    private FleetDaemon _homeDaemon = null!;
    private readonly System.Collections.Concurrent.ConcurrentQueue<(string Machine, string Tool, IReadOnlyDictionary<string, string> Arguments)> _headCalls = new();

    private static Endpoint NewEndpoint()
    {
        var name = $"fleet-test-{Guid.NewGuid():N}"[..20];
        return new Endpoint(OperatingSystem.IsWindows() ? name : Path.Combine(Path.GetTempPath(), name + ".sock"));
    }

    private FleetDaemon Start(Endpoint endpoint, FakePanes panes, Func<string, string, RemoteChannel>? open = null)
    {
        var daemon = new FleetDaemon(new DaemonOptions
        {
            Endpoint = endpoint,
            Pty = panes.NewPty,
            Terminal = panes.NewTerminal,
            FleetExecutable = "fleet",
            RemoteOpen = open,
            Notices = () => _farNotices,
            DismissNotices = (project, keys) => _farDismissed.Enqueue((project, keys)),
            AlertSettings = () => (true, true),
            Toast = (title, _) => _toasts.Enqueue(title),
            Head = (tool, arguments, _) =>
            {
                _headCalls.Enqueue((endpoint == _far ? "far" : "home", tool, arguments));
                return Task.FromResult((tool == "relay" ? "relayed" : "refused", tool != "relay"));
            },
            SavedProjects = () => ["homelab", "dormant"],
            OpenProject = async name =>
            {
                await using var opener = await DaemonTests.TestClient.ConnectAsync(endpoint, ClientRoles.Control, 0, 0, null);
                var opened = await opener.RequestAsync(new ControlRequest { Op = "spawn", Workspace = name, Cwd = ".", Args = ["opened-" + name] });
                return opened.Ok ? null : opened.Error;
            },
        });
        _running.Add(daemon.RunAsync(_stop.Token));
        return daemon;
    }

    public Task InitializeAsync()
    {
        _home = NewEndpoint();
        _far = NewEndpoint();
        Start(_far, _farPanes);
        _homeDaemon = Start(_home, _homePanes, (host, token) => _open!(host, token));
        _open = (_, _) =>
        {
            var stream = _far.ConnectAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            return new RemoteChannel(stream, null, stream);
        };
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

    private async Task<IReadOnlyList<RemoteDto>> RemotesAsync()
    {
        using var home = new EmbeddedDriver(_home);
        return await home.RemotesAsync();
    }

    private static async Task Eventually(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(50);
        }

        Assert.True(await condition());
    }

    [Fact]
    public async Task A_connected_remote_lists_its_projects_by_its_machine_name_until_it_is_disconnected()
    {
        await using (var far = await DaemonTests.TestClient.ConnectAsync(_far, ClientRoles.Control, 0, 0, null))
        {
            foreach (var project in (string[])["homelab", "scraper", "homelab~hidden"])
            {
                Assert.True((await far.RequestAsync(new ControlRequest { Op = "spawn", Workspace = project, Cwd = ".", Args = ["claude"] })).Ok);
            }
        }

        using var home = new EmbeddedDriver(_home);
        await home.ConnectRemoteAsync("red@far");

        await Eventually(async () => (await RemotesAsync()).SingleOrDefault() is { State: RemoteLink.Connected, Projects.Count: 3 });
        var remote = Assert.Single(await RemotesAsync());
        Assert.Equal(("red@far", Environment.MachineName), (remote.Host, remote.Name));
        Assert.Equal(["dormant", "homelab", "scraper"], remote.Projects);
        Assert.Equal(["homelab", "scraper"], remote.Running);

        await home.DisconnectRemoteAsync("red@far");
        Assert.Empty(await RemotesAsync());
    }

    [Fact]
    public async Task A_remote_that_cannot_be_reached_says_why_and_can_be_retried()
    {
        _open = (_, _) => throw new IOException("ssh: connect to host far port 22: Connection refused");
        using var home = new EmbeddedDriver(_home);

        await home.ConnectRemoteAsync("red@far");
        await Eventually(async () => (await RemotesAsync()).SingleOrDefault() is { State: RemoteLink.Failed });
        Assert.Contains("Connection refused", Assert.Single(await RemotesAsync()).Error, StringComparison.Ordinal);

        _open = (_, _) =>
        {
            var stream = _far.ConnectAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            return new RemoteChannel(stream, null, stream);
        };
        await home.ConnectRemoteAsync("red@far");
        await Eventually(async () => (await RemotesAsync()).SingleOrDefault() is { State: RemoteLink.Connected });
    }

    [Fact]
    public async Task Ssh_asks_through_fleetd_and_gets_the_answer_the_user_typed_once()
    {
        string? token = null;
        _open = (_, t) =>
        {
            token = t;
            var silent = new SilentStream();
            return new RemoteChannel(silent, null, silent);
        };
        using var home = new EmbeddedDriver(_home);
        await home.ConnectRemoteAsync("red@far");
        await Eventually(() => Task.FromResult(token is not null));

        Assert.Equal((true, (string?)null), await home.AskPassAsync(token!, "red@far's password: "));
        var asking = Assert.Single(await RemotesAsync());
        Assert.Equal((RemoteLink.Asking, "red@far's password: ", true), (asking.State, asking.Prompt, asking.Secret));

        await home.AnswerRemoteAsync("red@far", "hunter2");

        Assert.Equal((false, "hunter2"), await home.AskPassAsync(token!, "red@far's password: "));
        Assert.Equal((true, (string?)null), await home.AskPassAsync(token!, "red@far's password: "));
        await Assert.ThrowsAsync<Fleet.Ports.Mux.Exceptions.MuxUnavailableException>(() => home.AskPassAsync("someone-else", "password: "));
    }

    private async Task<DaemonTests.TestClient> ClientAsync(Endpoint endpoint, string role, int cols = 0, int rows = 0, string? workspace = null)
    {
        var client = await DaemonTests.TestClient.ConnectAsync(endpoint, role, cols, rows, workspace);
        _clients.Add(client);
        return client;
    }

    private static async Task<IReadOnlyList<PaneDto>> PanesAsync(DaemonTests.TestClient control) =>
        (await control.RequestAsync(new ControlRequest { Op = "list-panes" })).Panes ?? [];

    [Fact]
    public async Task A_remote_project_shown_here_draws_the_remote_screen_and_takes_keys_and_commands()
    {
        var far = await ClientAsync(_far, ClientRoles.Control);
        Assert.True((await far.RequestAsync(new ControlRequest { Op = "spawn", Workspace = "homelab", Cwd = ".", Args = ["remote-claude"] })).Ok);
        _farPanes.ByProgram("remote-claude")!.Emit("REMOTE-SCREEN");

        var home = await ClientAsync(_home, ClientRoles.Control);
        Assert.True((await home.RequestAsync(new ControlRequest { Op = "spawn", Workspace = "local", Cwd = ".", Args = ["shell"] })).Ok);
        var window = await ClientAsync(_home, ClientRoles.Attach, 80, 24, "local");
        await window.WaitForFramesAsync(1);

        Assert.True((await home.RequestAsync(new ControlRequest { Op = "remote-connect", Host = "red@far" })).Ok);
        await Eventually(async () => (await RemotesAsync()).SingleOrDefault() is { State: RemoteLink.Connected });

        var shown = await home.RequestAsync(new ControlRequest { Op = "show-remote", Client = window.Id, Host = "red@far", Workspace = "homelab" });
        Assert.True(shown.Ok, shown.Error);

        var workspace = FleetDaemon.RemoteWorkspace(Environment.MachineName);
        await Eventually(async () => (await PanesAsync(home)).Any(p => p.Session == workspace));
        var remotePane = (await PanesAsync(home)).Single(p => p.Session == workspace).Id;
        await Eventually(async () => (await home.RequestAsync(new ControlRequest { Op = "get-text", Pane = remotePane })).Text is { } text
            && text.Contains("REMOTE-SCREEN", StringComparison.Ordinal)
            && text.Contains($"homelab @{Environment.MachineName}", StringComparison.Ordinal));

        await window.SendKeyAsync("x");
        await Eventually(() => Task.FromResult(_farPanes.ByProgram("remote-claude")!.Written == "x"));
        Assert.Equal(string.Empty, _homePanes.ByProgram("shell")!.Written);

        await window.SendCommandAsync("split-right");
        await Eventually(async () => (await PanesAsync(far)).Count(p => p.Session == "homelab") == 2);

        Assert.False((await far.RequestAsync(new ControlRequest { Op = "hand-back", Client = "nobody", Text = "switch-project" })).Pending);
        var viewedFromHome = (await far.RequestAsync(new ControlRequest { Op = "hand-back", Client = "c1", Text = "switch-project" })).Pending;
        Assert.True(viewedFromHome);
        await Eventually(() => Task.FromResult(_homePanes.ByProgram("fleet") is { } menu
            && menu.Args.SequenceEqual(["menu", "--project", workspace, "--action", "switch-project"])));

        await window.SendCommandAsync("switch-project");
        await Eventually(() => Task.FromResult(_homePanes.Started.Count(p => p.Program == "fleet") == 1));

        Assert.True((await home.RequestAsync(new ControlRequest { Op = "remote-disconnect", Host = "red@far" })).Ok);
        await Eventually(async () => (await PanesAsync(home)).All(p => p.Session != workspace));
    }

    [Fact]
    public async Task A_session_furnishes_a_new_window_with_its_projects_and_reports_them_back_in_order()
    {
        var far = await ClientAsync(_far, ClientRoles.Control);
        Assert.True((await far.RequestAsync(new ControlRequest { Op = "spawn", Workspace = "homelab", Cwd = ".", Args = ["remote-claude"] })).Ok);

        var home = await ClientAsync(_home, ClientRoles.Control);
        foreach (var project in (string[])["fleet", "pc", "unrelated"])
        {
            Assert.True((await home.RequestAsync(new ControlRequest { Op = "spawn", Workspace = project, Cwd = ".", Args = ["shell-" + project] })).Ok);
        }

        Assert.True((await home.RequestAsync(new ControlRequest { Op = "remote-connect", Host = "red@far" })).Ok);
        await Eventually(async () => (await RemotesAsync()).SingleOrDefault() is { State: RemoteLink.Connected });

        var wire = new Wire(await _home.ConnectAsync(TimeSpan.FromSeconds(5)));
        await wire.SendAsync(
            MessageType.Hello,
            new Hello
            {
                Version = Wire.Version,
                Role = ClientRoles.Attach,
                Os = "test",
                Cols = 80,
                Rows = 24,
                Workspace = "fleet",
                Window = [new() { Name = "fleet" }, new() { Name = "homelab", Host = "red@far" }, new() { Name = "pc" }],
                Showing = new() { Name = "pc" },
            },
            WireJsonContext.Default.Hello);
        var client = Wire.Read((await wire.ReceiveAsync())!.Value.Payload, WireJsonContext.Default.Welcome).Client;
        var reader = Task.Run(async () =>
        {
            while (await wire.ReceiveAsync() is not null)
            {
            }
        });

        IReadOnlyList<WindowEntryDto> window = [];
        await Eventually(async () =>
        {
            window = (await home.RequestAsync(new ControlRequest { Op = "window", Client = client })).Window ?? [];
            return window.Any(e => e.Host is not null);
        });

        Assert.Equal(
            [("fleet", (string?)null, false), ("homelab", "red@far", false), ("pc", null, true)],
            window.Select(e => (e.Name, e.Host, e.Shown)));

        await wire.SendAsync(MessageType.Bye, ReadOnlyMemory<byte>.Empty);
        await reader.WaitAsync(TimeSpan.FromSeconds(10));
        wire.Dispose();
    }

    private async Task<(DaemonTests.TestClient Far, DaemonTests.TestClient Home, DaemonTests.TestClient Window, string Workspace)> ShowingHomelabAsync()
    {
        var far = await ClientAsync(_far, ClientRoles.Control);
        Assert.True((await far.RequestAsync(new ControlRequest { Op = "spawn", Workspace = "homelab", Cwd = ".", Args = ["remote-claude"] })).Ok);

        var home = await ClientAsync(_home, ClientRoles.Control);
        Assert.True((await home.RequestAsync(new ControlRequest { Op = "spawn", Workspace = "local", Cwd = ".", Args = ["shell"] })).Ok);
        var window = await ClientAsync(_home, ClientRoles.Attach, 80, 24, "local");
        await window.WaitForFramesAsync(1);

        Assert.True((await home.RequestAsync(new ControlRequest { Op = "remote-connect", Host = "red@far" })).Ok);
        await Eventually(async () => (await RemotesAsync()).SingleOrDefault() is { State: RemoteLink.Connected });
        Assert.True((await home.RequestAsync(new ControlRequest { Op = "show-remote", Client = window.Id, Host = "red@far", Workspace = "homelab" })).Ok);

        var workspace = FleetDaemon.RemoteWorkspace(Environment.MachineName);
        await Eventually(async () => (await PanesAsync(home)).Any(p => p.Session == workspace));
        await Eventually(async () => ((await home.RequestAsync(new ControlRequest { Op = "window", Client = window.Id })).Window ?? [])
            .Any(e => e is { Name: "homelab", Host: "red@far" }));
        return (far, home, window, workspace);
    }

    [Fact]
    public async Task The_fleet_menu_in_a_remote_project_is_the_remotes_menu_for_that_project()
    {
        var (_, _, window, _) = await ShowingHomelabAsync();

        await window.SendCommandAsync("menu");

        await Eventually(() => Task.FromResult(_farPanes.ByProgram("fleet") is { } menu
            && menu.Args.SequenceEqual(["menu", "--project", "homelab"])));
        Assert.Null(_homePanes.ByProgram("fleet"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("voice")]
    public async Task The_head_chord_in_a_remote_project_opens_the_origins_head_over_it(string? voice)
    {
        var (_, _, window, workspace) = await ShowingHomelabAsync();

        await window.SendCommandAsync("head", voice);

        await Eventually(() => Task.FromResult(_homePanes.ByProgram("fleet") is { } head
            && head.Args.SequenceEqual(voice is null ? ["head"] : ["head", "--voice"])));
        Assert.Equal(window.Id, _homePanes.ByProgram("fleet")!.Env[FleetDaemon.ClientVariable]);
        Assert.Null(_farPanes.ByProgram("fleet"));
        Assert.Contains(_homeDaemon.Model.HeadPane()!.Id, _homeDaemon.Model.PanesIn(workspace));

        await window.SendKeyAsync("x");
        await Eventually(() => Task.FromResult(_homePanes.ByProgram("fleet")!.Written == "x"));
        Assert.Equal(string.Empty, _farPanes.ByProgram("remote-claude")!.Written);
    }

    [Fact]
    public async Task A_head_tool_for_a_remote_runs_on_that_remotes_fleet_over_the_link()
    {
        await ShowingHomelabAsync();
        using var home = new EmbeddedDriver(_home);

        var relayed = await home.RemoteHeadAsync("red@far", "relay", new Dictionary<string, string> { ["project"] = "homelab", ["prompt"] = "go" });
        var refused = await home.RemoteHeadAsync("red@far", "switch_project", new Dictionary<string, string>());

        Assert.Equal(("relayed", false), (relayed.Text, relayed.ToolFailed));
        Assert.Equal(("refused", true), (refused.Text, refused.ToolFailed));
        Assert.All(_headCalls, c => Assert.Equal("far", c.Machine));
        Assert.Equal("go", _headCalls.First().Arguments["prompt"]);
        await Assert.ThrowsAsync<Fleet.Ports.Mux.Exceptions.MuxUnavailableException>(
            () => home.RemoteHeadAsync("red@nowhere", "relay", new Dictionary<string, string>()));
    }

    [Fact]
    public async Task A_new_project_on_a_remote_with_nothing_running_shows_it_here_and_opens_its_new_project_flow()
    {
        var home = await ClientAsync(_home, ClientRoles.Control);
        Assert.True((await home.RequestAsync(new ControlRequest { Op = "spawn", Workspace = "local", Cwd = ".", Args = ["shell"] })).Ok);
        var window = await ClientAsync(_home, ClientRoles.Attach, 80, 24, "local");
        await window.WaitForFramesAsync(1);

        Assert.True((await home.RequestAsync(new ControlRequest { Op = "remote-connect", Host = "red@far" })).Ok);
        await Eventually(async () => (await RemotesAsync()).SingleOrDefault() is { State: RemoteLink.Connected });

        var made = await home.RequestAsync(new ControlRequest { Op = "new-remote-project", Client = window.Id, Host = "red@far" });
        Assert.True(made.Ok, made.Error);

        var workspace = FleetDaemon.RemoteWorkspace(Environment.MachineName);
        await Eventually(async () => (await PanesAsync(home)).Any(p => p.Session == workspace));
        await Eventually(() => Task.FromResult(_farPanes.ByProgram("fleet") is { } menu
            && menu.Args.SequenceEqual(["menu", "--action", "new-project"])));
        Assert.Null(_homePanes.ByProgram("fleet"));
    }

    [Fact]
    public async Task Quitting_the_project_on_the_remote_closes_the_view_here_and_a_later_one_opens_again()
    {
        var (far, home, window, workspace) = await ShowingHomelabAsync();

        Assert.True((await far.RequestAsync(new ControlRequest { Op = "close-workspace", Workspace = "homelab" })).Ok);
        await Eventually(async () => (await PanesAsync(home)).All(p => p.Session != workspace));
        Assert.Equal(RemoteLink.Connected, Assert.Single(await RemotesAsync()).State);

        Assert.True((await far.RequestAsync(new ControlRequest { Op = "spawn", Workspace = "scraper", Cwd = ".", Args = ["scraper-claude"] })).Ok);
        _farPanes.ByProgram("scraper-claude")!.Emit("SCRAPER-SCREEN");
        Assert.True((await home.RequestAsync(new ControlRequest { Op = "show-remote", Client = window.Id, Host = "red@far", Workspace = "scraper" })).Ok);

        await Eventually(async () => (await PanesAsync(home)).SingleOrDefault(p => p.Session == workspace) is { } pane
            && (await home.RequestAsync(new ControlRequest { Op = "get-text", Pane = pane.Id })).Text?.Contains("SCRAPER-SCREEN", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task A_remote_project_moved_to_a_new_window_opens_one_here_attached_to_it()
    {
        var (far, _, window, _) = await ShowingHomelabAsync();

        Assert.True((await far.RequestAsync(new ControlRequest { Op = "open-window", Workspace = "homelab", Client = "c1" })).Ok);

        await Eventually(() => Task.FromResult(window.Effects.Any(e => e.Kind == HostEffects.OpenRemote && e.Value == "red@far\nhomelab")));
    }

    private static NoticeDto Notice(string project, string key, bool open = true) => new()
    {
        Project = project,
        Key = key,
        Kind = "NeedsInput",
        Worktree = "/home/red/" + key,
        Agent = "api / " + key,
        Message = "has a question for you",
        Since = new DateTime(2026, 9, 30, 9, 0, 0, DateTimeKind.Utc),
        Resolved = open ? null : new DateTime(2026, 9, 30, 9, 5, 0, DateTimeKind.Utc),
    };

    [Fact]
    public void Only_notices_that_opened_since_the_last_look_are_fresh()
    {
        List<NoticeDto> first = [Notice("homelab", "a")];
        List<NoticeDto> later = [Notice("homelab", "a"), Notice("homelab", "b"), Notice("homelab", "c", open: false)];

        Assert.Empty(RemoteLink.Fresh(null, first));
        Assert.Equal(["b"], RemoteLink.Fresh(new HashSet<string>(first.Select(RemoteLink.NoticeId)), later).Select(n => n.Key));
    }

    [Fact]
    public async Task A_remote_projects_notices_reach_the_window_showing_it_and_nothing_else_does()
    {
        _farNotices = [Notice("homelab", "old")];
        var (_, home, window, workspace) = await ShowingHomelabAsync();
        await window.SendCommandAsync("show", "local");

        await Eventually(() => Task.FromResult(_homeDaemon.Model.Notices(_homeDaemon.Model.Client(window.Id)!) == (0, 1)));
        Assert.DoesNotContain(window.Effects, e => e.Kind == HostEffects.Bell);
        Assert.Empty(_toasts);

        _farNotices = [Notice("homelab", "old"), Notice("scraper", "elsewhere")];
        await Task.Delay(TimeSpan.FromSeconds(3));
        Assert.DoesNotContain(window.Effects, e => e.Kind == HostEffects.Bell);

        _farNotices = [Notice("homelab", "old"), Notice("homelab", "new"), Notice("scraper", "elsewhere")];
        await Eventually(() => Task.FromResult(window.Effects.Any(e => e.Kind == HostEffects.Bell)));
        Assert.Equal([$"fleet · homelab @{Environment.MachineName}"], _toasts);
        Assert.Equal((0, 2), _homeDaemon.Model.Notices(_homeDaemon.Model.Client(window.Id)!));

        var listed = (await home.RequestAsync(new ControlRequest { Op = "remote-notices", Client = window.Id })).Notices!;
        Assert.Equal(["old", "new"], listed.Select(n => n.Key));
        Assert.All(listed, n => Assert.Equal(("red@far", Environment.MachineName), (n.Host, n.Machine)));

        Assert.True((await home.RequestAsync(new ControlRequest { Op = "remote-dismiss", Host = "red@far", Workspace = "homelab", Args = ["new"] })).Ok);
        await Eventually(() => Task.FromResult(_farDismissed.Any(d => d.Project == "homelab" && d.Keys.SequenceEqual(["new"]))));
        Assert.Contains(workspace, _homeDaemon.Model.Client(window.Id)!.Projects);
    }

    [Fact]
    public void A_remotes_list_is_its_saved_projects_and_whatever_else_runs_there()
    {
        Assert.Equal(
            ["default", "dormant", "homelab"],
            RemoteLink.Listed(["homelab", "dormant"], ["homelab", "default", "homelab~hidden"]));
    }

    [Fact]
    public async Task A_saved_remote_project_that_is_not_running_is_listed_and_started_there_when_shown()
    {
        var (far, home, window, _) = await ShowingHomelabAsync();

        var remote = Assert.Single(await RemotesAsync());
        Assert.Equal(["dormant", "homelab"], remote.Projects);
        Assert.Equal(["homelab"], remote.Running);

        Assert.True((await home.RequestAsync(new ControlRequest { Op = "show-remote", Client = window.Id, Host = "red@far", Workspace = "dormant" })).Ok);

        await Eventually(async () => (await PanesAsync(far)).Any(p => p.Session == "dormant"));
        await Eventually(async () => (await home.RequestAsync(new ControlRequest { Op = "window", Client = window.Id })).Window!
            .Any(e => e.Name == "dormant" && e.Host == "red@far"));
        Assert.NotNull(_farPanes.ByProgram("opened-dormant"));
    }

    [Fact]
    public async Task A_warm_menu_on_the_remote_hands_switch_project_back_through_its_pane()
    {
        var (far, _, window, workspace) = await ShowingHomelabAsync();

        await window.SendCommandAsync("menu");
        await Eventually(() => Task.FromResult(_farPanes.ByProgram("fleet") is not null));
        var menuPane = _farPanes.ByProgram("fleet")!.Env[FleetDaemon.PaneVariable];

        var handed = await far.RequestAsync(new ControlRequest { Op = "hand-back", Caller = menuPane, Text = "switch-project" });

        Assert.True(handed.Pending);
        await Eventually(() => Task.FromResult(_homePanes.ByProgram("fleet") is { } menu
            && menu.Args.SequenceEqual(["menu", "--project", workspace, "--action", "switch-project"])));
    }

    [Fact]
    public void A_remote_workspace_is_drawn_full_screen_and_never_saved_in_the_session()
    {
        var model = new Fleet.Platform.Mux.Embedded.Model.MuxModel();
        model.Spawn("local", ".", ["shell"]);
        var remote = model.Spawn("@homelab", string.Empty, ["remote", "red@far"]);
        model.Workspace("@homelab")!.RemoteHost = "red@far";
        var client = model.Connect(80, 24, "@homelab");
        model.Resizes();

        var view = model.View(client.Id)!;
        Assert.Equal(new Fleet.Platform.Mux.Embedded.Model.Rect(0, 0, 80, 24), Assert.Single(view.Panes).Area);
        Assert.Equal((80, 24), (remote.Cols, remote.Rows));
        Assert.Equal(["local"], model.Snapshot().Workspaces.Select(w => w.Name));
    }

    [Fact]
    public void A_host_key_question_is_not_a_secret()
    {
        Assert.False(RemoteLink.IsSecret("Are you sure you want to continue connecting (yes/no/[fingerprint])?"));
        Assert.True(RemoteLink.IsSecret("red@far's password: "));
    }

    [Fact]
    public void The_switcher_gets_open_all_this_machine_and_one_tab_per_remote_named_by_its_machine()
    {
        var tabs = SwitchTabs.For(
            [new PickerEntry("fleet", "this window"), new PickerEntry("pc")],
            ["fleet"],
            [new RemoteMachine("user@homelab", "homelab", RemoteState.Connected, ["api", "scraper"])]);

        Assert.Equal(["Open", "All", "this machine", "homelab"], tabs.Tabs.Select(t => t.Title));
        Assert.Equal(["fleet", "pc", "api", "scraper"], tabs.Tabs[1].Entries.Select(e => e.Label));
        Assert.Equal("homelab", tabs.Tabs[1].Entries[2].Detail);
        Assert.Equal(new SwitchTarget("scraper", "user@homelab"), tabs.Targets[tabs.MachineTab(0)][1]);
        Assert.Equal(new SwitchTarget("pc"), tabs.Targets[tabs.ThisMachine][1]);
    }

    private sealed class SilentStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => 0;

        public override long Position { get; set; }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => 0;

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }

        public override long Seek(long offset, SeekOrigin origin) => 0;

        public override void SetLength(long value)
        {
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
        }
    }
}