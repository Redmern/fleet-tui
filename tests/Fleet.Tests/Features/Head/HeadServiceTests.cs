using Fleet.Features.Head.ServeHead;
using Fleet.Features.Head.ServeHead.Models;
using Fleet.Features.Orchestrations.ListSubs;
using Fleet.Platform.Mux.Fake;
using Fleet.Ports;
using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;
using Fleet.Ports.Approvals;
using Fleet.Ports.Approvals.Models;
using Fleet.Ports.Mcp.Models;
using Fleet.Ports.Mux.Models;
using Fleet.Ports.Projects;
using Fleet.Ports.Projects.Models;
using Fleet.Ports.Remotes;
using Fleet.Ports.Remotes.Enums;
using Fleet.Ports.Remotes.Models;
using Fleet.Ports.Requests;
using Fleet.Ports.Settings;
using Fleet.Shared.Constants;
using Fleet.Shared.Keymap.Enums;
using Fleet.Shared.Settings.Enums;
using Fleet.Shared.Settings.Models;

namespace Fleet.Tests.Features.Head;

public sealed class HeadServiceTests
{
    private const string Idle = "╭────╮\n│ >  │\n╰────╯\n  ? for shortcuts";

    private const string Busy = "* Churning... (12s · esc to interrupt)";

    private static readonly Project Web = new("web", "C:/p/web");

    private static readonly Project Api = new("api", "C:/p/api");

    private static readonly HeadTiming Fast = new(
        TimeSpan.FromMilliseconds(5),
        TimeSpan.Zero,
        TimeSpan.Zero,
        TimeSpan.FromMilliseconds(500),
        TimeSpan.FromSeconds(5));

    private readonly FakeMuxDriver _mux = new();

    private readonly Settings _settings = new();

    private readonly Approvals _approvals = new();

    private readonly Agents _agents = new();

    private readonly Requests _requests = new();

    private readonly Workspaces _workspaces = new();

    private readonly Dictionary<string, (PaneId Main, PaneId Dash)> _open = new(StringComparer.OrdinalIgnoreCase);

    private readonly List<string> _opened = [];

    private readonly Remotes _remotes = new();

    private readonly Known _known = new();

    private readonly Dictionary<string, List<string>> _repositories = new(StringComparer.OrdinalIgnoreCase);

    private Task<ProjectStructure> Structure(Project project, CancellationToken ct)
    {
        var agents = _agents.List(project.Name);

        return Task.FromResult(new ProjectStructure(
            _repositories.GetValueOrDefault(project.Name) ?? [],
            SubSummary.Text(agents, a => a.Open),
            SubSummary.Unowned(agents, a => a.Open)));
    }

    private HeadService Service() => new(
        new HeadDeps(
            new Projects(Web, Api),
            _mux,
            _settings,
            _approvals,
            _agents,
            _requests,
            _workspaces,
            (p, _) => Task.FromResult(_open.ContainsKey(p.Name)),
            p =>
            {
                _opened.Add(p.Name);
                Open(p, Idle);
                return Task.FromResult<string?>(null);
            },
            name => _open.TryGetValue(name, out var panes) ? panes.Dash.Value : null,
            new NoLog(),
            _remotes,
            _known,
            Structure),
        Fast);

    private (PaneId Main, PaneId Dash) Open(Project project, string text)
    {
        var main = _mux.SpawnAsync(new SpawnOptions { Cwd = project.Root, SessionName = project.Name, NewWindow = true })
            .GetAwaiter().GetResult();
        var dash = _mux.SplitAsync(new SplitOptions(main, Fleet.Ports.Mux.Enums.SplitDirection.Right) { Cwd = project.Root })
            .GetAwaiter().GetResult();

        _mux.SetText(main, text);
        _mux.SetText(dash, "fleet — " + project.Name);
        _open[project.Name] = (main, dash);

        return (main, dash);
    }

    private static McpRequest Call(string tool, params (string Key, string Value)[] args) =>
        new(tool, args.ToDictionary(a => a.Key, a => a.Value));

    [Fact]
    public async Task Relay_types_the_dispatch_prompt_into_the_idle_orchestrator_not_the_dashboard()
    {
        var (main, dash) = Open(Web, Idle);

        var result = await Service().HandleAsync(
            Call(HeadTools.Relay, (HeadTools.Project, "web"), (HeadTools.Prompt, "add a login page")));

        Assert.False(result.IsError, result.Text);
        Assert.Equal([RelayText.IntoNvimTerminal + ",add a login page", RelayText.Submit], _mux.SentTo(main));
        Assert.Empty(_mux.SentTo(dash));
    }

    [Fact]
    public async Task Relay_types_straight_into_a_main_orchestrator_that_runs_without_nvim()
    {
        var (main, _) = Open(Web, Idle);
        _settings.Config = SettingsConfig.Default.WithMainOrchestratorInNvim(false);

        await Service().HandleAsync(Call(HeadTools.Relay, (HeadTools.Project, "web"), (HeadTools.Prompt, "go")));

        Assert.Equal([",go", RelayText.Submit], _mux.SentTo(main));
    }

    [Fact]
    public async Task Relay_uses_the_projects_own_trigger_and_never_doubles_it()
    {
        var (main, _) = Open(Web, Idle);
        _settings.Config = SettingsConfig.Default.WithTrigger(";");

        await Service().HandleAsync(Call(HeadTools.Relay, (HeadTools.Project, "web"), (HeadTools.Prompt, ";fix it")));

        Assert.Equal(RelayText.IntoNvimTerminal + ";fix it", _mux.SentTo(main)[0]);
    }

    [Fact]
    public async Task Relay_queues_while_the_orchestrator_is_busy_and_delivers_once_it_is_idle()
    {
        var (main, _) = Open(Web, Busy);
        var service = Service();

        var result = await service.HandleAsync(
            Call(HeadTools.Relay, (HeadTools.Project, "web"), (HeadTools.Prompt, "ship it")));

        Assert.False(result.IsError, result.Text);
        Assert.Contains("queued", result.Text);
        Assert.Equal(1, service.Relay.Pending("web"));
        Assert.Empty(_mux.SentTo(main));

        _mux.SetText(main, Idle);
        await service.Relay.Drained("web").WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(0, service.Relay.Pending("web"));
        Assert.Equal([RelayText.IntoNvimTerminal + ",ship it", RelayText.Submit], _mux.SentTo(main));
    }

    [Fact]
    public async Task Relay_keeps_order_behind_an_earlier_queued_prompt()
    {
        var (main, _) = Open(Web, Busy);
        var service = Service();

        await service.HandleAsync(Call(HeadTools.Relay, (HeadTools.Project, "web"), (HeadTools.Prompt, "first")));
        await service.HandleAsync(Call(HeadTools.Relay, (HeadTools.Project, "web"), (HeadTools.Prompt, "second")));

        Assert.Equal(2, service.Relay.Pending("web"));

        _mux.SetText(main, Idle);
        await service.Relay.Drained("web").WaitAsync(TimeSpan.FromSeconds(5));

        var typed = _mux.SentTo(main).Where(t => t != RelayText.Submit).ToList();
        Assert.Equal([RelayText.IntoNvimTerminal + ",first", RelayText.IntoNvimTerminal + ",second"], typed);
    }

    [Fact]
    public async Task Relay_into_a_closed_project_opens_it_then_delivers()
    {
        var result = await Service().HandleAsync(
            Call(HeadTools.Relay, (HeadTools.Project, "api"), (HeadTools.Prompt, "bump deps")));

        Assert.False(result.IsError, result.Text);
        Assert.Equal(["api"], _opened);
        Assert.StartsWith("opened api", result.Text);
        Assert.Equal(RelayText.IntoNvimTerminal + ",bump deps", _mux.SentTo(_open["api"].Main)[0]);
    }

    [Fact]
    public async Task A_forbidden_dispatch_refuses_before_opening_or_typing()
    {
        _settings.Config = SettingsConfig.Default.With(HarnessTool.Dispatch, ActionPolicy.Forbid);

        var result = await Service().HandleAsync(
            Call(HeadTools.Relay, (HeadTools.Project, "api"), (HeadTools.Prompt, "anything")));

        Assert.True(result.IsError);
        Assert.Empty(_opened);
        Assert.Contains("does not allow dispatch", result.Text);
    }

    [Fact]
    public async Task An_ask_dispatch_asks_in_that_project_and_a_denial_types_nothing()
    {
        var (main, _) = Open(Web, Idle);
        _settings.Config = SettingsConfig.Default.With(HarnessTool.Dispatch, ActionPolicy.Ask)
            .With(HarnessTool.Dispatch, AskChannel.ClaudePermission);
        _approvals.Answer = ApprovalOutcome.Deny("Declined by the user in fleet.");

        var result = await Service().HandleAsync(
            Call(HeadTools.Relay, (HeadTools.Project, "web"), (HeadTools.Prompt, "deploy")));

        Assert.True(result.IsError);
        Assert.Equal("Declined by the user in fleet.", result.Text);
        var asked = Assert.Single(_approvals.Asked);
        Assert.Equal("web", asked.Project);
        Assert.Equal("dispatch", asked.Tool);
        Assert.Contains(",deploy", asked.Summary);
        Assert.Empty(_mux.SentTo(main));
    }

    [Fact]
    public async Task An_allowed_ask_still_delivers()
    {
        var (main, _) = Open(Web, Idle);
        _settings.Config = SettingsConfig.Default.With(HarnessTool.Dispatch, ActionPolicy.Ask);

        var result = await Service().HandleAsync(
            Call(HeadTools.Relay, (HeadTools.Project, "web"), (HeadTools.Prompt, "deploy")));

        Assert.False(result.IsError, result.Text);
        Assert.Single(_approvals.Asked);
        Assert.NotEmpty(_mux.SentTo(main));
    }

    [Fact]
    public async Task Tell_types_the_message_as_is_without_the_dispatch_trigger()
    {
        var (main, _) = Open(Web, Idle);

        var result = await Service().HandleAsync(
            Call(HeadTools.Tell, (HeadTools.Project, "web"), (HeadTools.Prompt, "what is the status?")));

        Assert.False(result.IsError, result.Text);
        Assert.Equal([RelayText.IntoNvimTerminal + "what is the status?", RelayText.Submit], _mux.SentTo(main));
    }

    [Fact]
    public async Task Tell_strips_a_leading_trigger_so_it_can_never_dispatch()
    {
        var (main, _) = Open(Web, Idle);

        await Service().HandleAsync(Call(HeadTools.Tell, (HeadTools.Project, "web"), (HeadTools.Prompt, ",fix it")));

        Assert.Equal(RelayText.IntoNvimTerminal + "fix it", _mux.SentTo(main)[0]);
    }

    [Fact]
    public async Task Tell_is_gated_as_tell_agent_not_as_dispatch()
    {
        var (main, _) = Open(Web, Idle);
        _settings.Config = SettingsConfig.Default.With(HarnessTool.Dispatch, ActionPolicy.Forbid)
            .With(HarnessTool.TellAgent, ActionPolicy.Ask);

        var result = await Service().HandleAsync(
            Call(HeadTools.Tell, (HeadTools.Project, "web"), (HeadTools.Prompt, "status?")));

        Assert.False(result.IsError, result.Text);
        Assert.Equal("tell_agent", Assert.Single(_approvals.Asked).Tool);
        Assert.NotEmpty(_mux.SentTo(main));
    }

    [Fact]
    public async Task Tell_queues_while_the_orchestrator_is_busy()
    {
        var (main, _) = Open(Web, Busy);
        var service = Service();

        var result = await service.HandleAsync(
            Call(HeadTools.Tell, (HeadTools.Project, "web"), (HeadTools.Prompt, "status?")));

        Assert.Contains("queued", result.Text);
        _mux.SetText(main, Idle);
        await service.Relay.Drained("web").WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal([RelayText.IntoNvimTerminal + "status?", RelayText.Submit], _mux.SentTo(main));
    }

    [Fact]
    public async Task List_projects_says_which_are_open()
    {
        Open(Web, Idle);

        var result = await Service().HandleAsync(Call(HeadTools.ListProjects));

        Assert.Contains("web  open  C:/p/web", result.Text);
        Assert.Contains("api  closed  C:/p/api", result.Text);
    }

    [Fact]
    public async Task List_remote_projects_groups_projects_by_machine_with_this_machine_first()
    {
        Open(Web, Idle);
        _remotes.Machines =
        [
            new RemoteMachine("user@homelab", "homelab-01", RemoteState.Connected, ["site", "blog"], Running: ["site"]),
        ];
        _known.Remotes = [new KnownRemote("USER@HOMELAB", "lab", DateTimeOffset.UnixEpoch)];

        var result = await Service().HandleAsync(Call(HeadTools.ListRemoteProjects));

        Assert.False(result.IsError, result.Text);
        Assert.Equal(
            "this machine\n  api  closed\n  web  open\nlab (user@homelab)  connected\n  blog  closed\n  site  open",
            result.Text);
    }

    [Fact]
    public async Task List_remote_projects_lists_a_known_machine_that_is_not_connected_without_projects()
    {
        _known.Remotes =
        [
            new KnownRemote("pi@garage", null, DateTimeOffset.UnixEpoch),
            new KnownRemote("user@nas", "nas", DateTimeOffset.UnixEpoch.AddDays(1)),
        ];

        var result = await Service().HandleAsync(Call(HeadTools.ListRemoteProjects));

        Assert.EndsWith("\nnas (user@nas)  known · not connected\npi@garage  known · not connected", result.Text);
        Assert.Empty(_remotes.Connected);
    }

    [Fact]
    public async Task List_remote_projects_shows_the_state_of_a_link_that_is_not_connected_yet()
    {
        _remotes.Machines =
        [
            new RemoteMachine("user@homelab", "user@homelab", RemoteState.Asking, ["site"]),
            new RemoteMachine("pi@garage", "garage", RemoteState.Failed, [], Error: "host unreachable"),
        ];

        var result = await Service().HandleAsync(Call(HeadTools.ListRemoteProjects));

        Assert.Contains("\nuser@homelab  connecting · ssh is asking a question in Remote machines\n", result.Text);
        Assert.EndsWith("\ngarage (pi@garage)  failed: host unreachable", result.Text);
        Assert.DoesNotContain("site", result.Text);
    }

    [Fact]
    public async Task List_remote_projects_says_when_no_remote_machine_is_known()
    {
        var result = await Service().HandleAsync(Call(HeadTools.ListRemoteProjects));

        Assert.Equal("this machine\n  api  closed\n  web  closed\nno remote machines are known.", result.Text);
    }

    [Fact]
    public async Task Switch_project_focuses_the_dashboard_and_asks_for_its_workspace()
    {
        var (_, dash) = Open(Web, Idle);

        var result = await Service().HandleAsync(Call(HeadTools.SwitchProject, (HeadTools.Project, "web")));

        Assert.False(result.IsError, result.Text);
        Assert.Equal("web", Assert.Single(_workspaces.Submitted));
        Assert.True((await _mux.ListPanesAsync()).Single(p => p.Id == dash).IsActive);
        Assert.Empty(_opened);
    }

    [Fact]
    public async Task Switch_project_opens_a_closed_project_first_and_needs_no_permission()
    {
        _settings.Config = SettingsConfig.Default.With(HarnessTool.Dispatch, ActionPolicy.Forbid);

        var result = await Service().HandleAsync(Call(HeadTools.SwitchProject, (HeadTools.Project, "API")));

        Assert.False(result.IsError, result.Text);
        Assert.Equal(["api"], _opened);
        Assert.Empty(_approvals.Asked);
    }

    [Fact]
    public async Task Switch_project_on_a_multiplexer_with_workspaces_shows_the_project()
    {
        var mux = new FakeMuxDriver(workspaces: true);
        await mux.SpawnAsync(new SpawnOptions { Cwd = Web.Root, SessionName = "web" });

        var service = new HeadService(
            new HeadDeps(
                new Projects(Web), mux, _settings, _approvals, _agents, _requests, _workspaces,
                (_, _) => Task.FromResult(true), _ => Task.FromResult<string?>(null), _ => null, new NoLog(), _remotes, _known, Structure),
            Fast);

        await service.HandleAsync(Call(HeadTools.SwitchProject, (HeadTools.Project, "web")));

        Assert.Equal("web", mux.ShowingFor(mux.CurrentClient));
        Assert.Empty(_workspaces.Submitted);
    }

    [Fact]
    public async Task Menu_action_hands_a_dashboard_action_to_the_project()
    {
        Open(Web, Idle);

        var result = await Service().HandleAsync(
            Call(HeadTools.MenuAction, (HeadTools.Project, "web"), (HeadTools.Action, "add-repository")));

        Assert.False(result.IsError, result.Text);
        Assert.Equal(("web", FleetAction.AddRepository), Assert.Single(_requests.Submitted));
    }

    [Theory]
    [InlineData("close")]
    [InlineData("quit")]
    [InlineData("teleport")]
    public async Task Menu_action_refuses_what_the_dashboard_cannot_draw(string action)
    {
        Open(Web, Idle);

        var result = await Service().HandleAsync(
            Call(HeadTools.MenuAction, (HeadTools.Project, "web"), (HeadTools.Action, action)));

        Assert.True(result.IsError);
        Assert.Empty(_requests.Submitted);
    }

    [Fact]
    public async Task List_agents_covers_every_open_project_under_each_projects_rule()
    {
        Open(Web, Idle);
        Open(Api, Idle);
        _agents.Records["web"] = [new AgentRecord("C:/p/web/site/login", "site", "login", "nvim", "main", true, Open: true)];
        _agents.Records["api"] = [new AgentRecord("C:/p/api/core/fix", "core", "fix", "claude", "main", true)];
        _settings.PerProject["api"] = SettingsConfig.Default.With(HarnessTool.ListAgents, ActionPolicy.Forbid);

        var result = await Service().HandleAsync(Call(HeadTools.ListAgents));

        Assert.Contains("web  site/login  nvim  open", result.Text);
        Assert.Contains("api: api does not allow list_agents", result.Text);
        Assert.DoesNotContain("core/fix", result.Text);
    }

    private static AgentRecord Orchestrator(string slug, string status = "", string summary = "", string at = "") =>
        new($"C:/p/web/.fleet/orchestrations/{slug}", string.Empty, slug, AgentHarness.Orchestrator, "origin/main", false,
            Status: status, Summary: summary, ReportedAt: at);

    private static AgentRecord Worker(
        string repo, string branch, string owner = "", string status = "", string summary = "", string at = "", bool open = false) =>
        new($"C:/p/web/{repo}/{branch}", repo, branch, AgentHarness.Nvim, "origin/main", true,
            Open: open, Owner: owner, Status: status, Summary: summary, ReportedAt: at);

    [Fact]
    public async Task Project_structure_shows_repositories_each_sub_with_its_agents_and_the_agents_under_no_sub()
    {
        _repositories["web"] = ["api", "site"];
        _agents.Records["web"] =
        [
            Orchestrator("upgrade", OrchestrationStatus.Working, "two agents started", "2026-10-04T09:00:00Z"),
            Worker("site", "login", "upgrade", "done", "login page merged", "2026-10-04T09:30:00Z", open: true),
            Worker("api", "auth", "upgrade"),
            Worker("api", "hotfix", status: "idle", open: true),
        ];

        var result = await Service().HandleAsync(Call(HeadTools.ProjectStructure, (HeadTools.Project, "WEB")));

        Assert.False(result.IsError, result.Text);
        Assert.Equal(
            """
            web  C:/p/web
            repositories
              api
              site
            sub-orchestrators
              upgrade — working, pane closed
                last report 2026-10-04T09:00:00Z: two agents started
                - api/auth — closed, no report
                - site/login — open, done
                    last report 2026-10-04T09:30:00Z: login page merged
            agents not under a sub-orchestrator
              - api/hotfix — open, idle
            """.ReplaceLineEndings("\n"),
            result.Text);
        Assert.Empty(_remotes.Forwarded);
    }

    [Fact]
    public async Task Project_structure_of_a_project_without_subs_lists_its_agents_as_under_no_sub()
    {
        _agents.Records["api"] = [Worker("core", "fix", status: "working"), Worker("core", "docs")];

        var result = await Service().HandleAsync(Call(HeadTools.ProjectStructure, (HeadTools.Project, "api")));

        Assert.False(result.IsError, result.Text);
        Assert.Contains("repositories\n  none\nsub-orchestrators\n  " + SubSummary.None + "\n", result.Text);
        Assert.EndsWith(
            "agents not under a sub-orchestrator\n  - core/docs — closed, no report\n  - core/fix — closed, working",
            result.Text);
    }

    [Fact]
    public async Task Project_structure_says_when_every_agent_is_under_a_sub()
    {
        _agents.Records["web"] = [Orchestrator("upgrade"), Worker("site", "login", "upgrade")];

        var result = await Service().HandleAsync(Call(HeadTools.ProjectStructure, (HeadTools.Project, "web")));

        Assert.Contains("  - site/login — closed, no report", result.Text);
        Assert.EndsWith("agents not under a sub-orchestrator\n  " + SubSummary.NoUnowned, result.Text);
    }

    [Fact]
    public async Task Project_structure_keeps_each_section_under_its_own_permission()
    {
        _repositories["web"] = ["site"];
        _agents.Records["web"] = [Orchestrator("upgrade"), Worker("site", "solo")];
        _settings.PerProject["web"] = SettingsConfig.Default.With(HarnessTool.ListSubs, ActionPolicy.Forbid);

        var result = await Service().HandleAsync(Call(HeadTools.ProjectStructure, (HeadTools.Project, "web")));

        Assert.False(result.IsError, result.Text);
        Assert.Contains("sub-orchestrators\n  web does not allow list_subs", result.Text);
        Assert.DoesNotContain("upgrade", result.Text);
        Assert.Contains("repositories\n  site", result.Text);
        Assert.Contains("  - site/solo", result.Text);
    }

    [Fact]
    public async Task Project_structure_needs_a_known_project()
    {
        var missing = await Service().HandleAsync(Call(HeadTools.ProjectStructure));
        var unknown = await Service().HandleAsync(Call(HeadTools.ProjectStructure, (HeadTools.Project, "nope")));

        Assert.True(missing.IsError);
        Assert.Contains("'project' is required", missing.Text);
        Assert.True(unknown.IsError);
        Assert.Contains("web, api", unknown.Text);
    }

    [Fact]
    public async Task Project_structure_on_a_remote_comes_from_that_machines_fleet()
    {
        KnowHostinger();
        _remotes.Machines = [HostingerUp];
        _remotes.Reply = McpResult.Ok("shop  /srv/shop\nrepositories\n  store");

        var result = await Service().HandleAsync(
            Call(HeadTools.ProjectStructure, (HeadTools.Project, "SHOP"), (HeadTools.Remote, "hostinger")));

        Assert.False(result.IsError, result.Text);
        Assert.Equal("on hostinger: shop  /srv/shop\nrepositories\n  store", result.Text);
        var (host, forwarded) = Assert.Single(_remotes.Forwarded);
        Assert.Equal(Hostinger, host);
        Assert.Equal(HeadTools.ProjectStructure, forwarded.Tool);
        Assert.Equal("shop", forwarded.Value(HeadTools.Project));
        Assert.False(forwarded.Arguments.ContainsKey(HeadTools.Remote));
    }

    [Fact]
    public async Task Project_structure_on_a_remote_needs_a_project()
    {
        KnowHostinger();
        _remotes.Machines = [HostingerUp];

        var result = await Service().HandleAsync(Call(HeadTools.ProjectStructure, (HeadTools.Remote, "hostinger")));

        Assert.True(result.IsError);
        Assert.Contains("'project' is required", result.Text);
        Assert.Empty(_remotes.Forwarded);
    }

    [Fact]
    public async Task Served_to_the_origin_project_structure_runs_here()
    {
        _agents.Records["web"] = [Orchestrator("upgrade"), Worker("site", "login", "upgrade")];

        var result = await Service().ServeOriginAsync(Call(HeadTools.ProjectStructure, (HeadTools.Project, "web")));

        Assert.False(result.IsError, result.Text);
        Assert.Contains("upgrade — working", result.Text);
        Assert.Contains("    - site/login", result.Text);
        Assert.Empty(_remotes.Forwarded);
    }

    [Fact]
    public async Task An_unknown_project_names_the_ones_there_are()
    {
        var result = await Service().HandleAsync(Call(HeadTools.SwitchProject, (HeadTools.Project, "nope")));

        Assert.True(result.IsError);
        Assert.Contains("web, api", result.Text);
    }

    private const string Hostinger = "red@hostinger.example";

    private static readonly RemoteMachine HostingerUp =
        new(Hostinger, "srv-1", RemoteState.Connected, ["DeVrolijkeViervoeters", "shop"], Running: ["shop"]);

    private void KnowHostinger() =>
        _known.Remotes =
        [
            new KnownRemote(Hostinger, "hostinger", new DateTimeOffset(2026, 10, 1, 9, 30, 0, TimeSpan.Zero)),
            new KnownRemote("red@laptop", "laptop", DateTimeOffset.UnixEpoch),
        ];

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("LOCAL")]
    public async Task Without_a_remote_or_with_local_the_tools_act_on_the_origin(string? remote)
    {
        KnowHostinger();
        _remotes.Machines = [HostingerUp];
        var (_, dash) = Open(Web, Idle);
        var call = remote is null
            ? Call(HeadTools.SwitchProject, (HeadTools.Project, "web"))
            : Call(HeadTools.SwitchProject, (HeadTools.Project, "web"), (HeadTools.Remote, remote));

        var result = await Service().HandleAsync(call);

        Assert.False(result.IsError, result.Text);
        Assert.True((await _mux.ListPanesAsync()).Single(p => p.Id == dash).IsActive);
        Assert.Empty(_remotes.Shown);
        Assert.Empty(_remotes.Forwarded);
        Assert.Empty(_remotes.Connected);
    }

    [Fact]
    public async Task List_projects_without_a_remote_says_each_is_on_local()
    {
        var result = await Service().HandleAsync(Call(HeadTools.ListProjects));

        Assert.All(result.Text.Split('\n'), line => Assert.Contains("  on local", line));
    }

    [Theory]
    [InlineData("homelab")]
    [InlineData("red@hostinger.example")]
    public async Task An_unknown_nickname_or_a_raw_ssh_host_is_refused_and_names_the_machines(string remote)
    {
        KnowHostinger();

        var result = await Service().HandleAsync(
            Call(HeadTools.Relay, (HeadTools.Project, "shop"), (HeadTools.Prompt, "go"), (HeadTools.Remote, remote)));

        Assert.True(result.IsError);
        Assert.Contains($"no remote machine is nicknamed '{remote}'", result.Text);
        Assert.Contains("local, hostinger, laptop", result.Text);
        Assert.Empty(_remotes.Connected);
        Assert.Empty(_remotes.Forwarded);
    }

    [Fact]
    public async Task A_disconnected_remote_is_connected_first_then_the_project_is_shown_here()
    {
        KnowHostinger();
        _remotes.OnConnect = HostingerUp;

        var result = await Service().HandleAsync(
            Call(HeadTools.SwitchProject, (HeadTools.Project, "devrolijkeviervoeters"), (HeadTools.Remote, "Hostinger")));

        Assert.False(result.IsError, result.Text);
        Assert.Equal([Hostinger], _remotes.Connected);
        Assert.Equal((Hostinger, "DeVrolijkeViervoeters"), Assert.Single(_remotes.Shown));
        Assert.Equal([Hostinger], _known.Remembered);
        Assert.Equal("switched to DeVrolijkeViervoeters on hostinger.", result.Text);
        Assert.Empty(_workspaces.Submitted);
    }

    [Fact]
    public async Task A_remote_ssh_cannot_reach_says_so()
    {
        KnowHostinger();
        _remotes.OnConnect = new RemoteMachine(Hostinger, Hostinger, RemoteState.Failed, [], Error: "Connection timed out");

        var result = await Service().HandleAsync(
            Call(HeadTools.ListProjects, (HeadTools.Remote, "hostinger")));

        Assert.True(result.IsError);
        Assert.Equal("could not reach hostinger (red@hostinger.example) over ssh: Connection timed out.", result.Text);
    }

    [Fact]
    public async Task A_remote_whose_ssh_asks_a_question_points_the_user_at_remote_machines()
    {
        KnowHostinger();
        _remotes.OnConnect = new RemoteMachine(Hostinger, Hostinger, RemoteState.Asking, [], Prompt: "red@hostinger.example's password: ");

        var result = await Service().HandleAsync(
            Call(HeadTools.SwitchProject, (HeadTools.Project, "shop"), (HeadTools.Remote, "hostinger")));

        Assert.True(result.IsError);
        Assert.Contains("password", result.Text);
        Assert.Contains("Remote machines", result.Text);
        Assert.Empty(_remotes.Shown);
    }

    [Fact]
    public async Task A_remote_that_stays_connecting_times_out_with_a_reason()
    {
        KnowHostinger();
        _remotes.OnConnect = new RemoteMachine(Hostinger, Hostinger, RemoteState.Connecting, []);
        var service = new HeadService(
            new HeadDeps(
                new Projects(Web), _mux, _settings, _approvals, _agents, _requests, _workspaces,
                (_, _) => Task.FromResult(false), _ => Task.FromResult<string?>(null), _ => null, new NoLog(), _remotes, _known, Structure),
            Fast with { ConnectTimeout = TimeSpan.FromMilliseconds(50) });

        var result = await service.HandleAsync(Call(HeadTools.ListAgents, (HeadTools.Remote, "hostinger")));

        Assert.True(result.IsError);
        Assert.Contains("still connecting", result.Text);
    }

    [Fact]
    public async Task A_project_that_is_not_on_the_remote_names_the_ones_that_are()
    {
        KnowHostinger();
        _remotes.Machines = [HostingerUp];

        var result = await Service().HandleAsync(
            Call(HeadTools.Relay, (HeadTools.Project, "web"), (HeadTools.Prompt, "go"), (HeadTools.Remote, "hostinger")));

        Assert.True(result.IsError);
        Assert.Equal("no project named 'web' on hostinger. Projects there: DeVrolijkeViervoeters, shop.", result.Text);
        Assert.Empty(_remotes.Forwarded);
        Assert.Empty(_remotes.Connected);
    }

    [Fact]
    public async Task Relay_on_a_remote_goes_to_that_machines_fleet_without_the_remote_argument()
    {
        KnowHostinger();
        _remotes.Machines = [HostingerUp];
        _remotes.Reply = McpResult.Error("shop does not allow dispatch; change it in that project's permissions.");

        var result = await Service().HandleAsync(
            Call(HeadTools.Relay, (HeadTools.Project, "SHOP"), (HeadTools.Prompt, "ship"), (HeadTools.Remote, "hostinger")));

        Assert.True(result.IsError);
        Assert.Equal("on hostinger: shop does not allow dispatch; change it in that project's permissions.", result.Text);
        var (host, forwarded) = Assert.Single(_remotes.Forwarded);
        Assert.Equal(Hostinger, host);
        Assert.Equal(HeadTools.Relay, forwarded.Tool);
        Assert.Equal("shop", forwarded.Value(HeadTools.Project));
        Assert.Equal("ship", forwarded.Value(HeadTools.Prompt));
        Assert.False(forwarded.Arguments.ContainsKey(HeadTools.Remote));
        Assert.Empty(_approvals.Asked);
    }

    [Fact]
    public async Task Tell_on_a_remote_goes_to_that_machines_fleet()
    {
        KnowHostinger();
        _remotes.Machines = [HostingerUp];
        _remotes.Reply = McpResult.Ok("told shop's orchestrator: status?");

        var result = await Service().HandleAsync(
            Call(HeadTools.Tell, (HeadTools.Project, "shop"), (HeadTools.Prompt, "status?"), (HeadTools.Remote, "hostinger")));

        Assert.False(result.IsError, result.Text);
        var (_, forwarded) = Assert.Single(_remotes.Forwarded);
        Assert.Equal(HeadTools.Tell, forwarded.Tool);
        Assert.False(forwarded.Arguments.ContainsKey(HeadTools.Remote));
    }

    [Fact]
    public async Task Menu_action_on_a_remote_asks_its_dashboard_then_shows_it_here()
    {
        KnowHostinger();
        _remotes.Machines = [HostingerUp];
        _remotes.Reply = McpResult.Ok("asked shop's dashboard for new-agent.");

        var result = await Service().HandleAsync(
            Call(HeadTools.MenuAction, (HeadTools.Project, "shop"), (HeadTools.Action, "new-agent"), (HeadTools.Remote, "hostinger")));

        Assert.False(result.IsError, result.Text);
        Assert.Equal(HeadTools.MenuAction, Assert.Single(_remotes.Forwarded).Request.Tool);
        Assert.Equal((Hostinger, "shop"), Assert.Single(_remotes.Shown));
        Assert.Empty(_requests.Submitted);
    }

    [Fact]
    public async Task List_projects_on_a_remote_says_each_is_on_that_machine()
    {
        KnowHostinger();
        _remotes.Machines = [HostingerUp];

        var result = await Service().HandleAsync(Call(HeadTools.ListProjects, (HeadTools.Remote, "hostinger")));

        Assert.Equal("DeVrolijkeViervoeters  closed  on hostinger\nshop  open  on hostinger", result.Text);
    }

    [Fact]
    public async Task List_remotes_puts_local_first_then_each_remote_with_host_last_connection_and_state()
    {
        KnowHostinger();
        _remotes.Machines = [HostingerUp, new RemoteMachine("pi@garage", "garage", RemoteState.Connected, [])];

        var lines = (await Service().HandleAsync(Call(HeadTools.ListRemotes))).Text.Split('\n');

        Assert.StartsWith("local  ", lines[0]);
        Assert.StartsWith($"hostinger  {Hostinger}  connected  last connected ", lines[1]);
        Assert.Equal("laptop  red@laptop  not connected  last connected "
            + DateTimeOffset.UnixEpoch.ToLocalTime().ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture),
            lines[2]);
        Assert.StartsWith("(no nickname)  pi@garage  connected", lines[3]);
        Assert.Equal(4, lines.Length);
    }

    [Fact]
    public async Task Served_to_the_origin_a_menu_action_is_handed_over_without_showing_here()
    {
        var mux = new FakeMuxDriver(workspaces: true);
        await mux.SpawnAsync(new SpawnOptions { Cwd = Web.Root, SessionName = "web" });
        var service = new HeadService(
            new HeadDeps(
                new Projects(Web), mux, _settings, _approvals, _agents, _requests, _workspaces,
                (_, _) => Task.FromResult(true), _ => Task.FromResult<string?>(null), _ => null, new NoLog(), _remotes, _known, Structure),
            Fast);

        var result = await service.ServeOriginAsync(
            Call(HeadTools.MenuAction, (HeadTools.Project, "web"), (HeadTools.Action, "new-agent")));

        Assert.False(result.IsError, result.Text);
        Assert.Equal(("web", FleetAction.NewAgent), Assert.Single(_requests.Submitted));
        Assert.Null(mux.ShowingFor(mux.CurrentClient));
    }

    [Fact]
    public async Task Served_to_the_origin_the_projects_own_permissions_still_apply()
    {
        _settings.Config = SettingsConfig.Default.With(HarnessTool.Dispatch, ActionPolicy.Forbid);

        var result = await Service().ServeOriginAsync(
            Call(HeadTools.Relay, (HeadTools.Project, "web"), (HeadTools.Prompt, "go")));

        Assert.True(result.IsError);
        Assert.Contains("does not allow dispatch", result.Text);
        Assert.Empty(_opened);
    }

    [Theory]
    [InlineData(HeadTools.SwitchProject, null)]
    [InlineData(HeadTools.ListAgents, "laptop")]
    public async Task Served_to_the_origin_a_remote_never_shows_here_or_reaches_another_machine(string tool, string? remote)
    {
        KnowHostinger();
        var call = remote is null
            ? Call(tool, (HeadTools.Project, "web"))
            : Call(tool, (HeadTools.Remote, remote));

        var result = await Service().ServeOriginAsync(call);

        Assert.True(result.IsError);
        Assert.Empty(_remotes.Connected);
        Assert.Empty(_remotes.Forwarded);
        Assert.Empty(_workspaces.Submitted);
    }

    private sealed class Projects(params Project[] projects) : IProjectStore
    {
        public Project? Load(string name) =>
            projects.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

        public IReadOnlyList<Project> List() => projects;

        public void Save(Project project)
        {
        }

        public void Remove(string name)
        {
        }
    }

    private sealed class Settings : ISettingsStore
    {
        public SettingsConfig Config { get; set; } = SettingsConfig.Default;

        public Dictionary<string, SettingsConfig> PerProject { get; } = [];

        public SettingsConfig Load(string project) =>
            PerProject.TryGetValue(project, out var own) ? own : Config;

        public void Save(string project, SettingsConfig config)
        {
        }
    }

    private sealed class Approvals : IApprovalChannel
    {
        public ApprovalOutcome Answer { get; set; } = ApprovalOutcome.Allow;

        public List<ApprovalRequest> Asked { get; } = [];

        public Task<ApprovalOutcome> AskAsync(ApprovalRequest request, CancellationToken ct = default)
        {
            Asked.Add(request);
            return Task.FromResult(Answer);
        }
    }

    private sealed class Agents : IAgentStore
    {
        public Dictionary<string, List<AgentRecord>> Records { get; } = [];

        public void Save(string project, AgentRecord agent)
        {
        }

        public IReadOnlyList<AgentRecord> List(string project) =>
            Records.TryGetValue(project, out var records) ? records : [];

        public void Remove(string project, string worktree)
        {
        }
    }

    private sealed class Requests : IActionRequestStore
    {
        public List<(string, FleetAction)> Submitted { get; } = [];

        public void Submit(string project, FleetAction action) => Submitted.Add((project, action));

        public FleetAction TakePending(string project) => FleetAction.None;
    }

    private sealed class Workspaces : IWorkspaceRequestStore
    {
        public List<string> Submitted { get; } = [];

        public void Submit(string workspace) => Submitted.Add(workspace);
    }

    private sealed class Remotes : IRemoteMachines
    {
        public IReadOnlyList<RemoteMachine> Machines { get; set; } = [];

        public List<string> Connected { get; } = [];

        public RemoteMachine? OnConnect { get; set; }

        public List<(string Host, string Project)> Shown { get; } = [];

        public List<(string Host, McpRequest Request)> Forwarded { get; } = [];

        public McpResult Reply { get; set; } = McpResult.Ok("done");

        public Task<IReadOnlyList<RemoteMachine>> ListAsync(CancellationToken ct = default) => Task.FromResult(Machines);

        public Task ConnectAsync(string host, CancellationToken ct = default)
        {
            Connected.Add(host);

            if (OnConnect is { } connected)
            {
                Machines = [.. Machines.Where(m => m.Host != host), connected];
            }

            return Task.CompletedTask;
        }

        public Task AnswerAsync(string host, string answer, CancellationToken ct = default) => Task.CompletedTask;

        public Task DisconnectAsync(string host, CancellationToken ct = default) => Task.CompletedTask;

        public Task OpenInNewWindowAsync(string host, string project, CancellationToken ct = default) => Task.CompletedTask;

        public Task ShowHereAsync(string host, string project, CancellationToken ct = default)
        {
            Shown.Add((host, project));
            return Task.CompletedTask;
        }

        public Task NewProjectAsync(string host, CancellationToken ct = default) => Task.CompletedTask;

        public Task<McpResult> HeadAsync(string host, McpRequest request, CancellationToken ct = default)
        {
            Forwarded.Add((host, request));
            return Task.FromResult(Reply);
        }
    }

    private sealed class Known : IKnownRemoteStore
    {
        public IReadOnlyList<KnownRemote> Remotes { get; set; } = [];

        public List<string> Remembered { get; } = [];

        public IReadOnlyList<KnownRemote> Load() => Remotes;

        public void Remember(string host, DateTimeOffset connected) => Remembered.Add(host);

        public void Rename(string host, string? nickname)
        {
        }

        public void Forget(string host)
        {
        }
    }

    private sealed class NoLog : IFleetLog
    {
        public void Swallowed(Exception e)
        {
        }

        public void Write(string line)
        {
        }

        public IReadOnlyList<string> Tail(int lines) => [];
    }
}
