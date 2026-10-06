using Fleet.Cli.Composition;
using Fleet.Features.Head.ServeHead;
using Fleet.Features.Head.ServeHead.Models;
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

public sealed class HeadVisibilityTests
{
    private const string Hostinger = "red@hostinger.example";

    private static readonly Project Web = new("web", "C:/p/web");

    private static readonly AgentRecord Login =
        new("C:/p/web/site/login", "site", "login", AgentHarness.Claude, "main", true);

    private static readonly AgentRecord FixAuth =
        new("C:/p/web/.fleet/orchestrations/fix-auth", "orchestrations", "fix-auth", AgentHarness.Orchestrator, "", false);

    private static readonly AgentRecord Token =
        new("C:/p/web/api/token", "api", "token", AgentHarness.Claude, "main", true, Owner: "fix-auth");

    private readonly FakeMuxDriver _mux = new();

    private readonly Agents _agents = new();

    private readonly Settings _settings = new();

    private readonly Approvals _approvals = new();

    private readonly Remotes _remotes = new();

    private readonly Known _known = new();

    private readonly List<string> _opened = [];

    private readonly List<string> _trusted = [];

    private string? _window;

    private HeadService Service() => new(new HeadDeps(
        new Projects(Web),
        _mux,
        _settings,
        _approvals,
        _agents,
        new Requests(),
        new Workspaces(),
        (_, _) => Task.FromResult(_window is not null),
        p =>
        {
            _opened.Add(p.Name);
            OpenProject();
            return Task.FromResult<string?>(null);
        },
        _ => null,
        new NoLog(),
        _remotes,
        _known,
        (_, _) => throw new InvalidOperationException("show/hide never reads the project structure"),
        SetVisible: HeadPanes.SetVisible(_mux, _agents, _ => true, _trusted.Add)));

    private string OpenProject()
    {
        var dash = _mux.SpawnAsync(new SpawnOptions { Cwd = Web.Root, SessionName = Web.Name, NewWindow = true })
            .GetAwaiter().GetResult();

        _window = _mux.ListPanesAsync().GetAwaiter().GetResult().Single(p => p.Id == dash).WindowId;

        return _window;
    }

    private PaneId Shown(AgentRecord agent)
    {
        _agents.Save(Web.Name, agent with { Hidden = false, Open = true });

        return _mux.SpawnAsync(new SpawnOptions { Cwd = agent.Worktree, WindowId = _window }).GetAwaiter().GetResult();
    }

    private PaneId Hidden(AgentRecord agent)
    {
        _agents.Save(Web.Name, agent with { Hidden = true, Open = true });

        return _mux.SpawnAsync(new SpawnOptions { Cwd = agent.Worktree, Workspace = FleetWorkspaces.Hidden })
            .GetAwaiter().GetResult();
    }

    private Pane PaneOf(PaneId id) => _mux.ListPanesAsync().GetAwaiter().GetResult().Single(p => p.Id == id);

    private static McpRequest Call(string tool, params (string Key, string Value)[] args) =>
        new(tool, args.ToDictionary(a => a.Key, a => a.Value));

    private static McpRequest Agent(string tool, string repository, string branch, params (string Key, string Value)[] more) =>
        Call(tool, [(HeadTools.Project, "web"), (HeadTools.Repository, repository), (HeadTools.Branch, branch), .. more]);

    private static McpRequest Sub(string tool, string sub) =>
        Call(tool, (HeadTools.Project, "web"), (HeadTools.Sub, sub));

    [Fact]
    public async Task Show_agent_moves_a_hidden_agent_back_into_its_projects_window()
    {
        OpenProject();
        var pane = Hidden(Login);

        var result = await Service().HandleAsync(Agent(HeadTools.ShowAgent, "SITE", "login"));

        Assert.False(result.IsError, result.Text);
        Assert.Equal("site/login in web is now visible.", result.Text);
        Assert.Equal(_window, PaneOf(pane).WindowId);
        Assert.False(_agents.Get(Login).Hidden);
    }

    [Fact]
    public async Task Hide_agent_moves_a_visible_agent_out_of_sight_without_stopping_it()
    {
        OpenProject();
        var pane = Shown(Login);

        var result = await Service().HandleAsync(Agent(HeadTools.HideAgent, "site", "LOGIN"));

        Assert.False(result.IsError, result.Text);
        Assert.Equal("site/login in web is now hidden.", result.Text);
        Assert.Equal(FleetWorkspaces.Hidden, PaneOf(pane).SessionName);
        Assert.True(_agents.Get(Login).Hidden);
        Assert.True(_agents.Get(Login).Open);
    }

    [Fact]
    public async Task Show_agent_with_only_sub_shows_that_sub_orchestrator()
    {
        OpenProject();
        var pane = Hidden(FixAuth);

        var result = await Service().HandleAsync(Sub(HeadTools.ShowAgent, "Fix-Auth"));

        Assert.False(result.IsError, result.Text);
        Assert.Equal("sub-orchestrator fix-auth in web is now visible.", result.Text);
        Assert.Equal(_window, PaneOf(pane).WindowId);
        Assert.False(_agents.Get(FixAuth).Hidden);
    }

    [Fact]
    public async Task Hide_agent_with_only_sub_hides_that_sub_orchestrator()
    {
        OpenProject();
        var pane = Shown(FixAuth);

        var result = await Service().HandleAsync(Sub(HeadTools.HideAgent, "fix-auth"));

        Assert.False(result.IsError, result.Text);
        Assert.Equal("sub-orchestrator fix-auth in web is now hidden.", result.Text);
        Assert.Equal(FleetWorkspaces.Hidden, PaneOf(pane).SessionName);
    }

    [Fact]
    public async Task Show_agent_with_a_sub_shows_an_agent_that_sub_started()
    {
        OpenProject();
        _agents.Save(Web.Name, FixAuth);
        var pane = Hidden(Token);

        var result = await Service().HandleAsync(
            Agent(HeadTools.ShowAgent, "api", "token", (HeadTools.Sub, "fix-auth")));

        Assert.False(result.IsError, result.Text);
        Assert.Equal("api/token (under fix-auth) in web is now visible.", result.Text);
        Assert.Equal(_window, PaneOf(pane).WindowId);
    }

    [Fact]
    public async Task Hide_agent_with_a_sub_hides_an_agent_that_sub_started()
    {
        OpenProject();
        _agents.Save(Web.Name, FixAuth);
        var pane = Shown(Token);

        var result = await Service().HandleAsync(
            Agent(HeadTools.HideAgent, "api", "token", (HeadTools.Sub, "fix-auth")));

        Assert.False(result.IsError, result.Text);
        Assert.Equal("api/token (under fix-auth) in web is now hidden.", result.Text);
        Assert.Equal(FleetWorkspaces.Hidden, PaneOf(pane).SessionName);
    }

    [Fact]
    public async Task Showing_a_visible_pane_succeeds_and_moves_nothing()
    {
        OpenProject();
        Shown(Login);
        _settings.Config = SettingsConfig.Default.With(HarnessTool.SetAgentVisible, ActionPolicy.Forbid);

        var result = await Service().HandleAsync(Agent(HeadTools.ShowAgent, "site", "login"));

        Assert.False(result.IsError, result.Text);
        Assert.Equal("site/login in web is already visible.", result.Text);
        Assert.DoesNotContain("move", _mux.Calls);
        Assert.Empty(_approvals.Asked);
    }

    [Fact]
    public async Task Hiding_a_hidden_pane_succeeds_and_moves_nothing()
    {
        OpenProject();
        Hidden(FixAuth);

        var result = await Service().HandleAsync(Sub(HeadTools.HideAgent, "fix-auth"));

        Assert.False(result.IsError, result.Text);
        Assert.Equal("sub-orchestrator fix-auth in web is already hidden.", result.Text);
        Assert.DoesNotContain("move", _mux.Calls);
    }

    [Fact]
    public async Task Hiding_an_agent_that_is_not_running_succeeds_and_changes_nothing()
    {
        OpenProject();
        _agents.Save(Web.Name, Login);

        var result = await Service().HandleAsync(Agent(HeadTools.HideAgent, "site", "login"));

        Assert.False(result.IsError, result.Text);
        Assert.Equal("site/login in web is not running, so it has no pane to hide.", result.Text);
        Assert.False(_agents.Get(Login).Hidden);
    }

    [Fact]
    public async Task Show_agent_starts_a_stopped_agent_in_its_projects_window_and_keeps_focus()
    {
        var worktree = Directory.CreateTempSubdirectory("fleet-head-show-").FullName;

        try
        {
            OpenProject();
            var head = await _mux.SpawnAsync(new SpawnOptions { Cwd = "C:/head", NewWindow = true });
            await _mux.FocusPaneAsync(head);
            _mux.CurrentPane = head;
            var stopped = Login with { Worktree = worktree };
            _agents.Save(Web.Name, stopped);

            var result = await Service().HandleAsync(Agent(HeadTools.ShowAgent, "site", "login"));

            Assert.False(result.IsError, result.Text);
            Assert.Equal("started site/login in web; it is now visible.", result.Text);
            var started = (await _mux.ListPanesAsync()).Single(p => p.Cwd == worktree);
            Assert.Equal(_window, started.WindowId);
            Assert.True(PaneOf(head).IsActive);
            Assert.Equal([worktree], _trusted);
            Assert.True(_agents.Get(stopped).Open);
        }
        finally
        {
            Directory.Delete(worktree, recursive: true);
        }
    }

    [Fact]
    public async Task Show_agent_opens_a_closed_project_first()
    {
        Hidden(Login);

        var result = await Service().HandleAsync(Agent(HeadTools.ShowAgent, "site", "login"));

        Assert.False(result.IsError, result.Text);
        Assert.Equal(["web"], _opened);
        Assert.Equal("opened web; site/login in web is now visible.", result.Text);
    }

    [Fact]
    public async Task Hide_agent_never_opens_a_closed_project()
    {
        _agents.Save(Web.Name, Login with { Hidden = true });

        var result = await Service().HandleAsync(Agent(HeadTools.HideAgent, "site", "login"));

        Assert.False(result.IsError, result.Text);
        Assert.Empty(_opened);
    }

    [Fact]
    public async Task A_forbidden_set_agent_visible_refuses_and_moves_nothing()
    {
        OpenProject();
        Shown(Login);
        _settings.Config = SettingsConfig.Default.With(HarnessTool.SetAgentVisible, ActionPolicy.Forbid);

        var result = await Service().HandleAsync(Agent(HeadTools.HideAgent, "site", "login"));

        Assert.True(result.IsError);
        Assert.Contains("does not allow set_agent_visible", result.Text);
        Assert.DoesNotContain("move", _mux.Calls);
    }

    [Fact]
    public async Task An_ask_rule_asks_in_that_project_naming_the_target()
    {
        OpenProject();
        Shown(Login);
        _settings.Config = SettingsConfig.Default.With(HarnessTool.SetAgentVisible, ActionPolicy.Ask);
        _approvals.Answer = ApprovalOutcome.Deny("Declined by the user in fleet.");

        var result = await Service().HandleAsync(Agent(HeadTools.HideAgent, "site", "login"));

        Assert.True(result.IsError);
        var asked = Assert.Single(_approvals.Asked);
        Assert.Equal("web", asked.Project);
        Assert.Equal("set_agent_visible", asked.Tool);
        Assert.Contains("site/login in web", asked.Summary);
    }

    [Fact]
    public async Task An_unknown_project_names_the_ones_there_are()
    {
        var result = await Service().HandleAsync(
            Call(HeadTools.ShowAgent, (HeadTools.Project, "shop"), (HeadTools.Sub, "fix-auth")));

        Assert.True(result.IsError);
        Assert.Equal("no project named 'shop'. Projects: web.", result.Text);
    }

    [Fact]
    public async Task An_unknown_sub_names_the_sub_orchestrators_there_are()
    {
        _agents.Save(Web.Name, FixAuth);

        var result = await Service().HandleAsync(Sub(HeadTools.HideAgent, "nope"));

        Assert.True(result.IsError);
        Assert.Equal("no sub-orchestrator named 'nope' in web. Sub-orchestrators there: fix-auth.", result.Text);
    }

    [Fact]
    public async Task An_unknown_repository_names_the_ones_with_agents()
    {
        _agents.Save(Web.Name, Login);
        _agents.Save(Web.Name, FixAuth);

        var result = await Service().HandleAsync(Agent(HeadTools.ShowAgent, "docs", "login"));

        Assert.True(result.IsError);
        Assert.Equal("no agent in repository 'docs' in web. Repositories with agents there: site.", result.Text);
    }

    [Fact]
    public async Task An_unknown_branch_names_the_branches_of_that_repository()
    {
        _agents.Save(Web.Name, Login);

        var result = await Service().HandleAsync(Agent(HeadTools.HideAgent, "site", "signup"));

        Assert.True(result.IsError);
        Assert.Equal("no agent on branch 'signup' of site in web. Branches there: login.", result.Text);
    }

    [Fact]
    public async Task An_agent_that_another_sub_did_not_start_is_unknown_under_that_sub()
    {
        _agents.Save(Web.Name, FixAuth);
        _agents.Save(Web.Name, Login);

        var result = await Service().HandleAsync(
            Agent(HeadTools.ShowAgent, "site", "login", (HeadTools.Sub, "fix-auth")));

        Assert.True(result.IsError);
        Assert.Equal(
            "no agent in repository 'site' under sub-orchestrator fix-auth in web. Repositories with agents there: none.",
            result.Text);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("site", "")]
    [InlineData("", "login")]
    public async Task A_target_needs_repository_and_branch_or_a_sub(string repository, string branch)
    {
        _agents.Save(Web.Name, Login);

        var result = await Service().HandleAsync(Agent(HeadTools.ShowAgent, repository, branch));

        Assert.True(result.IsError);
        Assert.Contains($"'{HeadTools.Repository}'", result.Text);
        Assert.Empty(_opened);
    }

    [Fact]
    public async Task On_a_remote_show_agent_goes_to_that_machines_fleet_without_the_remote_argument()
    {
        _known.Remotes = [new KnownRemote(Hostinger, "hostinger", DateTimeOffset.UnixEpoch)];
        _remotes.Machines = [new RemoteMachine(Hostinger, "srv-1", RemoteState.Connected, ["shop"], Running: ["shop"])];
        _remotes.Reply = McpResult.Ok("api/token (under fix-auth) in shop is now visible.");

        var result = await Service().HandleAsync(Call(
            HeadTools.ShowAgent,
            (HeadTools.Project, "SHOP"),
            (HeadTools.Repository, "api"),
            (HeadTools.Branch, "token"),
            (HeadTools.Sub, "fix-auth"),
            (HeadTools.Remote, "hostinger")));

        Assert.False(result.IsError, result.Text);
        Assert.Equal("on hostinger: api/token (under fix-auth) in shop is now visible.", result.Text);
        var (host, forwarded) = Assert.Single(_remotes.Forwarded);
        Assert.Equal(Hostinger, host);
        Assert.Equal(HeadTools.ShowAgent, forwarded.Tool);
        Assert.Equal("shop", forwarded.Value(HeadTools.Project));
        Assert.Equal("fix-auth", forwarded.Value(HeadTools.Sub));
        Assert.False(forwarded.Arguments.ContainsKey(HeadTools.Remote));
        Assert.Empty(_remotes.Shown);
    }

    [Fact]
    public async Task On_a_remote_an_unknown_project_names_the_ones_there()
    {
        _known.Remotes = [new KnownRemote(Hostinger, "hostinger", DateTimeOffset.UnixEpoch)];
        _remotes.Machines = [new RemoteMachine(Hostinger, "srv-1", RemoteState.Connected, ["shop"])];

        var result = await Service().HandleAsync(Call(
            HeadTools.HideAgent, (HeadTools.Project, "web"), (HeadTools.Sub, "x"), (HeadTools.Remote, "hostinger")));

        Assert.True(result.IsError);
        Assert.Equal("no project named 'web' on hostinger. Projects there: shop.", result.Text);
        Assert.Empty(_remotes.Forwarded);
    }

    [Fact]
    public async Task Served_to_the_origin_hide_agent_acts_on_this_machines_project()
    {
        OpenProject();
        var pane = Shown(Login);

        var result = await Service().ServeOriginAsync(Agent(HeadTools.HideAgent, "site", "login"));

        Assert.False(result.IsError, result.Text);
        Assert.Equal("site/login in web is now hidden.", result.Text);
        Assert.Equal(FleetWorkspaces.Hidden, PaneOf(pane).SessionName);
    }

    [Fact]
    public void Both_tools_are_offered_with_the_target_and_remote_arguments()
    {
        foreach (var name in new[] { HeadTools.ShowAgent, HeadTools.HideAgent })
        {
            var spec = Assert.Single(HeadTools.All, t => t.Name == name);

            Assert.Equal(
                [HeadTools.Project, HeadTools.Repository, HeadTools.Branch, HeadTools.Sub, HeadTools.Remote],
                spec.Params.Select(p => p.Name));
            Assert.Contains($"`{name}`", HeadBrief.Text);
        }
    }

    private sealed class Agents : IAgentStore
    {
        private readonly Dictionary<string, List<AgentRecord>> _records = new(StringComparer.OrdinalIgnoreCase);

        public AgentRecord Get(AgentRecord agent) =>
            _records.Values.SelectMany(r => r).Single(a => a.Worktree == agent.Worktree);

        public void Save(string project, AgentRecord agent)
        {
            if (!_records.TryGetValue(project, out var records))
            {
                records = [];
                _records[project] = records;
            }

            records.RemoveAll(a => a.Worktree == agent.Worktree);
            records.Add(agent);
        }

        public IReadOnlyList<AgentRecord> List(string project) =>
            _records.TryGetValue(project, out var records) ? [.. records] : [];

        public void Remove(string project, string worktree)
        {
        }
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

        public SettingsConfig Load(string project) => Config;

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

    private sealed class Requests : IActionRequestStore
    {
        public void Submit(string project, FleetAction action)
        {
        }

        public FleetAction TakePending(string project) => FleetAction.None;
    }

    private sealed class Workspaces : IWorkspaceRequestStore
    {
        public void Submit(string workspace)
        {
        }
    }

    private sealed class Remotes : IRemoteMachines
    {
        public IReadOnlyList<RemoteMachine> Machines { get; set; } = [];

        public List<(string Host, string Project)> Shown { get; } = [];

        public List<(string Host, McpRequest Request)> Forwarded { get; } = [];

        public McpResult Reply { get; set; } = McpResult.Ok("done");

        public Task<IReadOnlyList<RemoteMachine>> ListAsync(CancellationToken ct = default) => Task.FromResult(Machines);

        public Task ConnectAsync(string host, CancellationToken ct = default) => Task.CompletedTask;

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

        public IReadOnlyList<KnownRemote> Load() => Remotes;

        public void Remember(string host, DateTimeOffset connected)
        {
        }

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
