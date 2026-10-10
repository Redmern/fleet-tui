using Fleet.Features.Head.ServeHead.Models;
using Fleet.Ports.Agents.Models;
using Fleet.Ports.Mcp.Models;
using Fleet.Ports.Mux.Enums;
using Fleet.Ports.Projects.Models;
using Fleet.Ports.Remotes.Enums;
using Fleet.Ports.Remotes.Models;
using Fleet.Shared.Constants;
using Fleet.Shared.Keymap;
using Fleet.Shared.Keymap.Enums;
using Fleet.Shared.Settings.Enums;

namespace Fleet.Features.Head.ServeHead;

public sealed class HeadService(HeadDeps deps, HeadTiming? timing = null)
{
    public const string ThisMachine = "this machine";

    private const string Indent = "  ";

    private readonly HeadGate _gate = new(deps);

    private readonly HeadRemotes _remotes = new(deps, timing ?? HeadTiming.Default);

    private HeadRelay? _relay;

    private readonly HeadVisibility _visibility = new(deps, new HeadGate(deps));

    private readonly HeadForwards _forwards = new(deps);

    public HeadRelay Relay => _relay ??= new HeadRelay(deps, _gate, timing ?? HeadTiming.Default);

    public static IReadOnlyList<FleetAction> MenuActions { get; } =
        [.. DashboardActions.Served.Where(a => a != FleetAction.Close)];

    public async Task<McpResult> HandleAsync(McpRequest request, CancellationToken ct = default)
    {
        var remote = request.Value(HeadTools.Remote).Trim();

        return request.Tool switch
        {
            HeadTools.ListRemotes => await _remotes.ListAsync(ct).ConfigureAwait(false),
            HeadTools.ListRemoteProjects => await ListRemoteProjectsAsync(ct).ConfigureAwait(false),
            HeadTools.ListForwards or HeadTools.ForwardPort or HeadTools.UnforwardPort or HeadTools.OpenUrl
                or HeadTools.StartStack or HeadTools.StopStack => await _forwards.HandleAsync(request, ct).ConfigureAwait(false),
            HeadTools.ListProjects or HeadTools.SwitchProject or HeadTools.MenuAction or HeadTools.ListAgents
                or HeadTools.ProjectStructure or HeadTools.Relay or HeadTools.Tell or HeadTools.ShowAgent
                or HeadTools.HideAgent when !HeadRemotes.IsLocal(remote) =>
                await _remotes.HandleAsync(remote, request, ct).ConfigureAwait(false),
            _ => await HandleHereAsync(request, show: true, ct).ConfigureAwait(false),
        };
    }

    public async Task<McpResult> ServeOriginAsync(McpRequest request, CancellationToken ct = default)
    {
        if (!HeadRemotes.IsLocal(request.Value(HeadTools.Remote).Trim()))
        {
            return McpResult.Error(
                "this fleet acts on its own projects only; the machine fleet was opened on reaches the others.");
        }

        return request.Tool is HeadTools.MenuAction or HeadTools.ListAgents or HeadTools.ProjectStructure
                or HeadTools.Relay or HeadTools.Tell or HeadTools.ShowAgent or HeadTools.HideAgent
            ? await HandleHereAsync(request, show: false, ct).ConfigureAwait(false)
            : McpResult.Error($"{request.Tool} is not served to another machine.");
    }

    private async Task<McpResult> HandleHereAsync(McpRequest request, bool show, CancellationToken ct) =>
        request.Tool switch
        {
            HeadTools.ListProjects => await ListProjectsAsync(ct).ConfigureAwait(false),
            HeadTools.SwitchProject => await WithProject(request, p => SwitchAsync(p, ct)).ConfigureAwait(false),
            HeadTools.MenuAction => await WithProject(request, p => MenuAsync(p, request, show, ct)).ConfigureAwait(false),
            HeadTools.ListAgents => await ListAgentsAsync(request, ct).ConfigureAwait(false),
            HeadTools.ProjectStructure => await WithProject(request, p => StructureAsync(p, ct)).ConfigureAwait(false),
            HeadTools.Relay => await WithProject(request, p => RelayAsync(p, request, ct)).ConfigureAwait(false),
            HeadTools.Tell => await WithProject(request, p => TellAsync(p, request, ct)).ConfigureAwait(false),
            HeadTools.ShowAgent => await WithProject(request, p => _visibility.SetAsync(p, request, visible: true, ct))
                .ConfigureAwait(false),
            HeadTools.HideAgent => await WithProject(request, p => _visibility.SetAsync(p, request, visible: false, ct))
                .ConfigureAwait(false),
            _ => McpResult.Error($"the head has no tool named '{request.Tool}'."),
        };

    public static string? MenuActionError(McpRequest request) =>
        MenuActions.Contains(FleetActionIds.Parse(request.Value(HeadTools.Action)))
            ? null
            : $"'{request.Value(HeadTools.Action).Trim()}' is not a menu action the head can run; use one of "
              + string.Join(", ", MenuActions.Select(FleetActionIds.For)) + ".";

    private async Task<McpResult> ListProjectsAsync(CancellationToken ct)
    {
        var projects = deps.Projects.List();

        if (projects.Count == 0)
        {
            return McpResult.Ok("fleet has no projects yet.");
        }

        var lines = new List<string>();

        foreach (var project in projects.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
        {
            var open = await deps.IsOpen(project, ct).ConfigureAwait(false);
            var queued = Relay.Pending(project.Name);

            lines.Add($"{project.Name}  {(open ? "open" : "closed")}  {project.Root}  on {HeadTools.Local}"
                + (queued > 0 ? $"  {queued} prompt(s) queued" : string.Empty));
        }

        return McpResult.Ok(string.Join('\n', lines));
    }

    private async Task<McpResult> ListRemoteProjectsAsync(CancellationToken ct)
    {
        var lines = new List<string> { ThisMachine };
        var projects = deps.Projects.List();

        if (projects.Count == 0)
        {
            lines.Add(Indent + "no projects");
        }

        foreach (var project in projects.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
        {
            var open = await deps.IsOpen(project, ct).ConfigureAwait(false);
            lines.Add($"{Indent}{project.Name}  {(open ? "open" : "closed")}");
        }

        var live = await deps.Remotes.ListAsync(ct).ConfigureAwait(false);
        var known = deps.KnownRemotes.Load()
            .DistinctBy(k => k.Host, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(k => k.Host, StringComparer.OrdinalIgnoreCase);

        foreach (var machine in live)
        {
            var label = known.GetValueOrDefault(machine.Host)?.Nickname ?? machine.Label;
            lines.Add($"{Machine(label, machine.Host)}  {Describe(machine)}");

            if (machine.State != RemoteState.Connected)
            {
                continue;
            }

            if (machine.Projects.Count == 0)
            {
                lines.Add(Indent + "no projects");
            }

            lines.AddRange(machine.Projects
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .Select(p => $"{Indent}{p}  {(machine.IsRunning(p) ? "open" : "closed")}"));
        }

        var connected = live.Select(m => m.Host).ToHashSet(StringComparer.OrdinalIgnoreCase);

        lines.AddRange(known.Values
            .Where(k => !connected.Contains(k.Host))
            .OrderByDescending(k => k.LastConnected)
            .Select(k => $"{Machine(k.Label, k.Host)}  known · not connected"));

        if (live.Count == 0 && known.Count == 0)
        {
            lines.Add("no remote machines are known.");
        }

        return McpResult.Ok(string.Join('\n', lines));
    }

    private static string Machine(string label, string host) =>
        string.Equals(label, host, StringComparison.OrdinalIgnoreCase) ? host : $"{label} ({host})";

    private static string Describe(RemoteMachine machine) =>
        machine.State switch
        {
            RemoteState.Connected => "connected",
            RemoteState.Asking => "connecting · ssh is asking a question in Remote machines",
            RemoteState.Failed => "failed" + (machine.Error is { Length: > 0 } error ? $": {error}" : string.Empty),
            _ => "connecting",
        };

    private async Task<McpResult> SwitchAsync(Project project, CancellationToken ct)
    {
        var opened = await OpenAsync(project, ct).ConfigureAwait(false);

        if (opened.Error is { } error)
        {
            return McpResult.Error(error);
        }

        await ShowAsync(project, ct).ConfigureAwait(false);

        return McpResult.Ok(opened.Opened
            ? $"opened {project.Name} and switched to it."
            : $"switched to {project.Name}.");
    }

    private async Task<McpResult> MenuAsync(Project project, McpRequest request, bool show, CancellationToken ct)
    {
        if (MenuActionError(request) is { } invalid)
        {
            return McpResult.Error(invalid);
        }

        var action = FleetActionIds.Parse(request.Value(HeadTools.Action));
        var opened = await OpenAsync(project, ct).ConfigureAwait(false);

        if (opened.Error is { } error)
        {
            return McpResult.Error(error);
        }

        deps.Requests.Submit(project.Name, action);

        if (!show)
        {
            return McpResult.Ok($"asked {project.Name}'s dashboard for {FleetActionIds.For(action)}.");
        }

        await ShowAsync(project, ct).ConfigureAwait(false);

        return McpResult.Ok(
            $"asked {project.Name}'s dashboard for {FleetActionIds.For(action)} and switched to it.");
    }

    private async Task<McpResult> ListAgentsAsync(McpRequest request, CancellationToken ct)
    {
        var name = request.Value(HeadTools.Project).Trim();
        List<Project> projects;

        if (name.Length > 0)
        {
            if (Find(name) is not { } one)
            {
                return Unknown(name);
            }

            projects = [one];
        }
        else
        {
            projects = [];

            foreach (var project in deps.Projects.List())
            {
                if (await deps.IsOpen(project, ct).ConfigureAwait(false))
                {
                    projects.Add(project);
                }
            }

            if (projects.Count == 0)
            {
                return McpResult.Ok("no project is open.");
            }
        }

        var lines = new List<string>();

        foreach (var project in projects)
        {
            if (await _gate.CheckAsync(project.Name, HarnessTool.ListAgents, string.Empty, ct)
                    .ConfigureAwait(false) is { } denied)
            {
                lines.Add($"{project.Name}: {denied}");
                continue;
            }

            var agents = deps.Agents.List(project.Name);

            if (agents.Count == 0)
            {
                lines.Add($"{project.Name}: no agents.");
                continue;
            }

            lines.AddRange(agents.Select(a => $"{project.Name}  {Describe(a)}"));
        }

        return McpResult.Ok(string.Join('\n', lines));
    }

    private async Task<McpResult> StructureAsync(Project project, CancellationToken ct)
    {
        var structure = await deps.Structure(project, ct).ConfigureAwait(false);
        var lines = new List<string> { $"{project.Name}  {project.Root}" };

        async Task Section(string title, HarnessTool tool, IEnumerable<string> body)
        {
            lines.Add(title);

            var denied = await _gate.CheckAsync(project.Name, tool, string.Empty, ct).ConfigureAwait(false);

            lines.AddRange(denied is null ? body.Select(l => Indent + l) : [Indent + denied]);
        }

        await Section(
                "repositories",
                HarnessTool.ListRepositories,
                structure.Repositories.Count == 0 ? ["none"] : structure.Repositories)
            .ConfigureAwait(false);
        await Section("sub-orchestrators", HarnessTool.ListSubs, structure.Subs.Split('\n')).ConfigureAwait(false);
        await Section("agents not under a sub-orchestrator", HarnessTool.ListAgents, structure.Agents.Split('\n'))
            .ConfigureAwait(false);

        return McpResult.Ok(string.Join('\n', lines));
    }

    private async Task<McpResult> RelayAsync(Project project, McpRequest request, CancellationToken ct)
    {
        var prompt = request.Value(HeadTools.Prompt).Trim();

        return prompt.Length == 0
            ? McpResult.Error($"'{HeadTools.Prompt}' is required for {HeadTools.Relay}.")
            : await Relay.RelayAsync(project, prompt, ct).ConfigureAwait(false);
    }

    private async Task<McpResult> TellAsync(Project project, McpRequest request, CancellationToken ct)
    {
        var prompt = request.Value(HeadTools.Prompt).Trim();

        return prompt.Length == 0
            ? McpResult.Error($"'{HeadTools.Prompt}' is required for {HeadTools.Tell}.")
            : await Relay.TellAsync(project, prompt, ct).ConfigureAwait(false);
    }

    private async Task<(bool Opened, string? Error)> OpenAsync(Project project, CancellationToken ct)
    {
        if (await deps.IsOpen(project, ct).ConfigureAwait(false))
        {
            return (false, null);
        }

        var failed = await deps.EnsureOpen(project).ConfigureAwait(false);

        return failed is null ? (true, null) : (false, $"could not open {project.Name}: {failed}");
    }

    private async Task ShowAsync(Project project, CancellationToken ct)
    {
        if (deps.Mux.Caps.HasFlag(MuxCaps.Workspaces))
        {
            await deps.Mux.ShowWorkspaceAsync(project.Name, ct).ConfigureAwait(false);
            return;
        }

        var panes = await deps.Mux.ListPanesAsync(ct).ConfigureAwait(false);
        var target = MainPane.Dashboard(panes, deps.DashPane(project.Name))
            ?? MainPane.Find(panes, project.Root, null);

        if (target is null)
        {
            return;
        }

        await deps.Mux.FocusPaneAsync(target.Id, ct).ConfigureAwait(false);
        deps.Workspaces.Submit(target.SessionName.Length == 0 ? FleetWorkspaces.Default : target.SessionName);
    }

    private async Task<McpResult> WithProject(McpRequest request, Func<Project, Task<McpResult>> act)
    {
        var name = request.Value(HeadTools.Project).Trim();

        if (name.Length == 0)
        {
            return McpResult.Error($"'{HeadTools.Project}' is required for {request.Tool}.");
        }

        return Find(name) is { } project ? await act(project).ConfigureAwait(false) : Unknown(name);
    }

    private Project? Find(string name) =>
        deps.Projects.List().FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    private McpResult Unknown(string name) =>
        McpResult.Error(
            $"no project named '{name}'. Projects: "
            + string.Join(", ", deps.Projects.List().Select(p => p.Name)) + ".");

    private static string Describe(AgentRecord agent)
    {
        var what = AgentHarness.IsOrchestrator(agent.Harness)
            ? $"sub-orchestrator {agent.Branch}"
            : $"{agent.Repository}/{agent.Branch}  {AgentHarness.Describe(agent.Harness)}";

        var state = agent.Status.Length > 0 ? agent.Status : agent.Open ? "open" : "stopped";

        return $"{what}  {state}" + (agent.Hidden ? "  (hidden)" : string.Empty);
    }
}
