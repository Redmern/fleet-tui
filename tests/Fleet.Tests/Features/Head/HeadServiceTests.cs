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
using Fleet.Ports.Requests;
using Fleet.Ports.Settings;
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
            new NoLog()),
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
    public async Task List_projects_says_which_are_open()
    {
        Open(Web, Idle);

        var result = await Service().HandleAsync(Call(HeadTools.ListProjects));

        Assert.Contains("web  open  C:/p/web", result.Text);
        Assert.Contains("api  closed  C:/p/api", result.Text);
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
                (_, _) => Task.FromResult(true), _ => Task.FromResult<string?>(null), _ => null, new NoLog()),
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

    [Fact]
    public async Task An_unknown_project_names_the_ones_there_are()
    {
        var result = await Service().HandleAsync(Call(HeadTools.SwitchProject, (HeadTools.Project, "nope")));

        Assert.True(result.IsError);
        Assert.Contains("web, api", result.Text);
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
