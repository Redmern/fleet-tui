using Fleet.Features.Agents;
using Fleet.Features.Agents.ChangeHarness;
using Fleet.Features.Agents.HideAgent;
using Fleet.Features.Agents.ListAgents;
using Fleet.Features.Agents.NewAgent;
using Fleet.Features.Agents.NewAgent.Models;
using Fleet.Features.Agents.OpenAgent;
using Fleet.Features.Agents.RemoveAgent;
using Fleet.Features.Agents.RemoveAgent.Models;
using Fleet.Features.Agents.StopAgent;
using Fleet.Features.Agents.TellAgent;
using Fleet.Features.Forwards.ForwardPorts;
using Fleet.Features.Forwards.ForwardPorts.Enums;
using Fleet.Features.Mcp.ServeMcp;
using Fleet.Features.Orchestrations.Dispatch;
using Fleet.Features.Orchestrations.Dispatch.Models;
using Fleet.Features.Orchestrations.ListSubs;
using Fleet.Features.Orchestrations.ReportStatus;
using Fleet.Features.Repositories.AddRepository;
using Fleet.Features.Repositories.AddRepository.Models;
using Fleet.Features.Repositories.ListRepositories;
using Fleet.Features.Repositories.ListRepositories.Models;
using Fleet.Features.Repositories.PullRepository;
using Fleet.Features.Repositories.RemoveRepository;
using Fleet.Features.Repositories.Secrets;
using Fleet.Features.Repositories.SetDefaultBranch;
using Fleet.Ports;
using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;
using Fleet.Ports.Git;
using Fleet.Ports.Harness;
using Fleet.Ports.Mcp.Models;
using Fleet.Ports.Mux;
using Fleet.Ports.Mux.Models;
using Fleet.Shared;
using Fleet.Shared.Constants;
using Fleet.Shared.Messaging;
using Fleet.Shared.Orchestrations;
using Fleet.Shared.Results;
using Fleet.Shared.Settings;
using Fleet.Shared.Settings.Enums;

namespace Fleet.Cli.Composition;

public sealed class McpActions(
    string project,
    string root,
    string caller,
    IGitRunner git,
    IMuxDriver mux,
    IAgentStore store,
    IHarnessConfig harnessConfig,
    IFleetLog log) : IAgentStarter
{
    private readonly ListAgentsHandler _agents = new(store);

    private readonly ListRepositoriesHandler _repos = new(git);

    private readonly ListBranchesHandler _branches = new(git);

    private readonly NewAgentHandler _spawner = new(git, mux, store, Adapters.Models, Adapters.SubagentGuidance);

    private readonly HideAgentHandler _hider = new(mux, store);

    private readonly StopAgentHandler _stopper = new(mux, store);

    private readonly RemoveAgentHandler _remover = new(git, mux, store);

    private readonly RemoveSubHandler _subRemover = new(new RemoveAgentHandler(git, mux, store), store);

    private readonly ChangeHarnessHandler _harnesses = new(store);

    private readonly AddRepositoryHandler _adder = new(git);

    private readonly PullRepositoryHandler _puller = new(git);

    private readonly RemoveRepositoryHandler _repoRemover = new(git);

    private readonly SetDefaultBranchHandler _defaults = new(git);

    private readonly SecretsHandler _secrets = new();

    private readonly ReportStatusHandler _reporter = new(store, inboxes: Adapters.AgentInboxes());

    private readonly TellAgentHandler _teller = new(mux, inboxes: Adapters.AgentInboxes());

    private readonly BranchStates _states = new(git);

    private readonly ForwardPortsHandler _forwardPorts = new(Adapters.Forwards(), Adapters.Browser(), Adapters.KnownRemotes());

    public async Task<McpResult> PerformAsync(McpRequest request, CancellationToken ct)
    {
        var tool = HarnessToolIds.Parse(request.Tool);

        return tool switch
        {
            HarnessTool.ListAgents => Ok(ToolText.Agents(_agents.Handle(project))),
            HarnessTool.ListRepositories => await ListRepositories(ct).ConfigureAwait(false),
            HarnessTool.ListBranches => await ListBranches(request, ct).ConfigureAwait(false),
            HarnessTool.AgentStatus => AgentStatus(request),
            HarnessTool.RepositoryStatus => await RepositoryStatus(request, ct).ConfigureAwait(false),
            HarnessTool.LogTail => LogTail(request),
            HarnessTool.NewAgent => await NewAgent(request, ct).ConfigureAwait(false),
            HarnessTool.TellAgent => await TellAgent(request, ct).ConfigureAwait(false),
            HarnessTool.OpenAgent => await OpenAgent(request, ct).ConfigureAwait(false),
            HarnessTool.SetAgentVisible => await SetVisible(request, ct).ConfigureAwait(false),
            HarnessTool.StopAgent => await StopAgent(request, ct).ConfigureAwait(false),
            HarnessTool.RemoveAgent => await RemoveAgent(request, ct).ConfigureAwait(false),
            HarnessTool.ChangeHarness => ChangeHarness(request),
            HarnessTool.AddRepository => await AddRepository(request, ct).ConfigureAwait(false),
            HarnessTool.PullRepository => await PullRepository(request, ct).ConfigureAwait(false),
            HarnessTool.RemoveRepository => await RemoveRepository(request, ct).ConfigureAwait(false),
            HarnessTool.SetDefaultBranch => await SetDefaultBranch(request, ct).ConfigureAwait(false),
            HarnessTool.DistributeSecrets => await DistributeSecrets(request, ct).ConfigureAwait(false),
            HarnessTool.Dispatch => await Dispatch(request, ct).ConfigureAwait(false),
            HarnessTool.ListSubs => await ListSubs(ct).ConfigureAwait(false),
            HarnessTool.StopSub => await StopSub(request, ct).ConfigureAwait(false),
            HarnessTool.RemoveSub => await RemoveSub(request, ct).ConfigureAwait(false),
            HarnessTool.Report => await Report(request, ct).ConfigureAwait(false),
            HarnessTool.ListForwards => await Forward(request, ForwardVerb.List, ct).ConfigureAwait(false),
            HarnessTool.ForwardPort => await Forward(request, ForwardVerb.Add, ct).ConfigureAwait(false),
            HarnessTool.UnforwardPort => await Forward(request, ForwardVerb.Remove, ct).ConfigureAwait(false),
            HarnessTool.OpenUrl => await Forward(request, ForwardVerb.Open, ct).ConfigureAwait(false),
            HarnessTool.StartStack => await Forward(request, ForwardVerb.Start, ct).ConfigureAwait(false),
            HarnessTool.StopStack => await Forward(request, ForwardVerb.Stop, ct).ConfigureAwait(false),
            _ => McpResult.Error($"{request.Tool} is not available."),
        };
    }

    private async Task<McpResult> Forward(McpRequest request, ForwardVerb verb, CancellationToken ct)
    {
        var order = McpForwards.Order(request, verb);
        if (!order.Succeeded)
        {
            return McpResult.Error(order.Error ?? "bad arguments");
        }

        var done = await _forwardPorts.HandleAsync(order.Value, ct).ConfigureAwait(false);
        return done.Succeeded ? Ok(done.Value) : McpResult.Error(done.Error ?? "failed");
    }

    private McpResult LogTail(McpRequest request)
    {
        var lines = log.Tail(ToolArguments.Count(request, ToolArguments.Lines, 200));

        return Ok(lines.Count == 0 ? "The log is empty." : string.Join('\n', lines));
    }

    private async Task<McpResult> ListRepositories(CancellationToken ct)
    {
        var summaries = await _repos.HandleAsync(root, ct).ConfigureAwait(false);

        return Ok(ToolText.Repositories([.. summaries.Select(s => s.Name)]));
    }

    private async Task<McpResult> ListBranches(McpRequest request, CancellationToken ct)
    {
        var repo = await Resolve(request, ct).ConfigureAwait(false);

        if (repo is null)
        {
            return Missing(request, ToolArguments.Repository) ?? NoRepository(request);
        }

        var branches = await _branches.HandleAsync(repo.Path, ct).ConfigureAwait(false);

        return Ok(branches.Count == 0
            ? $"{repo.Name} has no branches yet."
            : string.Join('\n', branches.Select(b => b.Reference)));
    }

    private McpResult AgentStatus(McpRequest request)
    {
        if (Missing(request, ToolArguments.Repository, ToolArguments.Branch) is { } error)
        {
            return error;
        }

        var agent = Find(request);

        return agent is null
            ? McpResult.Error(ToolText.NotFound(Repo(request), Branch(request)))
            : Ok(ToolText.Agent(agent, _states.For(agent.Worktree, agent.BaseRef)));
    }

    private async Task<McpResult> RepositoryStatus(McpRequest request, CancellationToken ct)
    {
        var repo = await Resolve(request, ct).ConfigureAwait(false);

        if (repo is null)
        {
            return Missing(request, ToolArguments.Repository) ?? NoRepository(request);
        }

        var state = await _repoRemover.InspectAsync(repo.Path, ct).ConfigureAwait(false);

        var worktrees = state.Worktrees.Count;
        var unpushed = state.Unpushed.Count;

        return Ok(
            $"{repo.Name} (default {repo.DefaultBranch}): {worktrees} worktree(s), "
            + $"{unpushed} branch(es) not pushed.");
    }

    private async Task<McpResult> NewAgent(McpRequest request, CancellationToken ct)
    {
        var repo = await Resolve(request, ct).ConfigureAwait(false);

        if (Missing(request, ToolArguments.Repository, ToolArguments.Branch) is { } error)
        {
            return error;
        }

        if (repo is null)
        {
            return NoRepository(request);
        }

        var started = await Start(repo, Branch(request), caller, ToolArguments.Text(request, ToolArguments.Task), ct)
            .ConfigureAwait(false);

        return started.Succeeded ? Ok(started.Value!) : McpResult.Error(started.Error!);
    }

    public async Task<Result<string>> StartAsync(AgentStart request, CancellationToken ct = default)
    {
        var repo = await Named(request.Repository, ct).ConfigureAwait(false);

        return repo is null
            ? Result<string>.Fail($"No repository named '{request.Repository}' in this project.")
            : await Start(repo, request.Branch, caller, request.Task, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyCollection<string>> BranchesAsync(
        string project, string repository, CancellationToken ct = default)
    {
        var repo = await Named(repository, ct).ConfigureAwait(false);

        if (repo is null)
        {
            return [];
        }

        var branches = await _branches.HandleAsync(repo.Path, ct).ConfigureAwait(false);

        return [.. branches.Select(b => b.ShortName)];
    }

    private async Task<Result<string>> Start(
        RepositorySummary repo, string branch, string owner, string task, CancellationToken ct)
    {
        var created = await _spawner
            .HandleAsync(
                new NewAgentCommand(
                    project, repo.Name, repo.Path, branch, repo.DefaultBranch,
                    AgentHarness.Nvim, owner, Claude: true),
                ct)
            .ConfigureAwait(false);

        if (!created.Succeeded)
        {
            return Result<string>.Fail(created.Error!);
        }

        if (owner.Trim().Length > 0)
        {
            store.Save(project, created.Value! with { Owner = owner });
        }

        ClaudeWiring.ApproveFolder(project, created.Value!.Worktree, repo.Name, branch);

        var name = SessionNames.RepoAgent(project, created.Value!.Repository, created.Value!.Branch);
        var session = $" Its Claude session is named \"{name}\".";

        if (task.Length == 0)
        {
            return Result<string>.Ok($"started {repo.Name}/{branch}.{session}");
        }

        var delivered = await SendWhenReady(created.Value!, task, ct).ConfigureAwait(false);

        return Result<string>.Ok(delivered
            ? $"started {repo.Name}/{branch} and gave it its first task.{session}"
            : $"started {repo.Name}/{branch}, but it was not ready to take the task; "
              + $"use tell_agent once it is up.{session}");
    }

    private async Task<bool> SendWhenReady(AgentRecord agent, string message, CancellationToken ct)
    {
        var marker = OrchestrationPaths.ReadyMarker(agent.Worktree);

        for (var i = 0; i < 80 && !File.Exists(marker); i++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(300), ct).ConfigureAwait(false);
        }

        if (!File.Exists(marker))
        {
            return false;
        }

        var pane = await PaneFor(agent, ct).ConfigureAwait(false);

        if (pane is null)
        {
            return false;
        }

        await _teller.DeliverAsync(agent, pane.Value, message, ct).ConfigureAwait(false);

        return true;
    }

    private async Task<McpResult> TellAgent(McpRequest request, CancellationToken ct)
    {
        if (Missing(request, ToolArguments.Repository, ToolArguments.Branch, ToolArguments.Message)
            is { } error)
        {
            return error;
        }

        var agent = Find(request);

        if (agent is null)
        {
            return McpResult.Error(ToolText.NotFound(Repo(request), Branch(request)));
        }

        var pane = await PaneFor(agent, ct).ConfigureAwait(false);

        if (pane is null)
        {
            return McpResult.Error(
                $"{Repo(request)}/{Branch(request)} is not open; open it first, then tell it.");
        }

        var message = ToolArguments.Text(request, ToolArguments.Message);
        var address = await _teller
            .RouteAsync(agent, pane.Value, message, ToolArguments.Flag(request, ToolArguments.Typed), ct)
            .ConfigureAwait(false);

        if (address is not null)
        {
            log.Write(LogTag.For(project, $"tell_agent hands {Repo(request)}/{Branch(request)} to SendMessage"));

            return Ok(PeerMessage.SendYourself(
                $"{Repo(request)}/{Branch(request)}",
                address,
                TellAgentHandler.InstructionFor(agent, message),
                $"If SendMessage can't reach that address, call {HarnessToolIds.For(HarnessTool.TellAgent)} again "
                + $"with {ToolArguments.Typed}: true."));
        }

        return Ok($"sent to {Repo(request)}/{Branch(request)}.");
    }

    private async Task<PaneId?> PaneFor(AgentRecord agent, CancellationToken ct)
    {
        var panes = await mux.ListPanesAsync(ct).ConfigureAwait(false);
        var pane = panes.FirstOrDefault(p => AgentPanes.Owns(p, agent) && !SubBrowse.Is(p));

        return pane?.Id;
    }

    private bool SubOrchestratorsInNvim() => Adapters.SubOrchestratorsInNvim(project);

    private async Task<McpResult> OpenAgent(McpRequest request, CancellationToken ct)
    {
        var agent = Find(request);

        if (Missing(request, ToolArguments.Repository, ToolArguments.Branch) is { } error)
        {
            return error;
        }

        if (agent is null)
        {
            return McpResult.Error(ToolText.NotFound(Repo(request), Branch(request)));
        }

        ClaudeWiring.TrustFolder(agent.Worktree);

        var outcome = await new OpenAgentHandler(mux, store, SubOrchestratorsInNvim(), models: Adapters.Models(project))
            .HandleAsync(project, agent, root, ct)
            .ConfigureAwait(false);

        return From(outcome, $"opened {Repo(request)}/{Branch(request)}.");
    }

    private async Task<McpResult> SetVisible(McpRequest request, CancellationToken ct)
    {
        var agent = Find(request);

        if (Missing(request, ToolArguments.Repository, ToolArguments.Branch, ToolArguments.Visible)
            is { } error)
        {
            return error;
        }

        if (agent is null)
        {
            return McpResult.Error(ToolText.NotFound(Repo(request), Branch(request)));
        }

        var wantVisible = ToolArguments.Flag(request, ToolArguments.Visible);

        if (agent.Hidden != wantVisible)
        {
            return Ok($"{Repo(request)}/{Branch(request)} is already "
                + (wantVisible ? "visible." : "hidden."));
        }

        var outcome = await _hider.HandleAsync(project, agent, null, ct: ct).ConfigureAwait(false);

        return outcome.Succeeded
            ? Ok($"{Repo(request)}/{Branch(request)} is now "
                + (outcome.Value!.Hidden ? "hidden." : "visible."))
            : McpResult.Error(outcome.Error!);
    }

    private async Task<McpResult> StopAgent(McpRequest request, CancellationToken ct)
    {
        var agent = Find(request);

        if (Missing(request, ToolArguments.Repository, ToolArguments.Branch) is { } error)
        {
            return error;
        }

        if (agent is null)
        {
            return McpResult.Error(ToolText.NotFound(Repo(request), Branch(request)));
        }

        var outcome = await _stopper.HandleAsync(project, agent, ct).ConfigureAwait(false);

        return From(outcome, $"stopped {Repo(request)}/{Branch(request)}.");
    }

    private async Task<McpResult> RemoveAgent(McpRequest request, CancellationToken ct)
    {
        var agent = Find(request);

        if (Missing(request, ToolArguments.Repository, ToolArguments.Branch) is { } error)
        {
            return error;
        }

        if (agent is null)
        {
            return McpResult.Error(ToolText.NotFound(Repo(request), Branch(request)));
        }

        var delete = ToolArguments.Flag(request, ToolArguments.DeleteWorktree);

        var outcome = await _remover.HandleAsync(project, agent, delete, ct).ConfigureAwait(false);

        return From(outcome, delete
            ? $"removed {Repo(request)}/{Branch(request)} and its worktree."
            : $"removed {Repo(request)}/{Branch(request)}, kept its files.");
    }

    private McpResult ChangeHarness(McpRequest request)
    {
        var agent = Find(request);

        if (Missing(request, ToolArguments.Repository, ToolArguments.Branch, ToolArguments.Harness)
            is { } error)
        {
            return error;
        }

        if (agent is null)
        {
            return McpResult.Error(ToolText.NotFound(Repo(request), Branch(request)));
        }

        var changed = _harnesses.Handle(project, agent, ToolArguments.Text(request, ToolArguments.Harness));

        return changed.Succeeded
            ? Ok($"{Repo(request)}/{Branch(request)} now opens "
                + $"{AgentHarness.Describe(changed.Value!.Harness)}.")
            : McpResult.Error(changed.Error!);
    }

    private async Task<McpResult> AddRepository(McpRequest request, CancellationToken ct)
    {
        if (Missing(request, ToolArguments.Repository) is { } error)
        {
            return error;
        }

        var created = await _adder
            .HandleAsync(AddRepositoryCommand.CreateNew(root, Repo(request), "main"), ct)
            .ConfigureAwait(false);

        return created.Succeeded
            ? Ok($"added repository {Repo(request)}.")
            : McpResult.Error(created.Error!);
    }

    private async Task<McpResult> PullRepository(McpRequest request, CancellationToken ct)
    {
        var repo = await Resolve(request, ct).ConfigureAwait(false);

        if (repo is null)
        {
            return Missing(request, ToolArguments.Repository) ?? NoRepository(request);
        }

        var pulled = await _puller.HandleAsync(repo.Path, repo.DefaultBranch, ct).ConfigureAwait(false);

        return pulled.Succeeded ? Ok(pulled.Value!) : McpResult.Error(pulled.Error!);
    }

    private async Task<McpResult> RemoveRepository(McpRequest request, CancellationToken ct)
    {
        var repo = await Resolve(request, ct).ConfigureAwait(false);

        if (repo is null)
        {
            return Missing(request, ToolArguments.Repository) ?? NoRepository(request);
        }

        var removed = _repoRemover.Handle(repo.Path);

        return removed.Succeeded
            ? Ok($"removed repository {repo.Name}.")
            : McpResult.Error(removed.Error!);
    }

    private async Task<McpResult> SetDefaultBranch(McpRequest request, CancellationToken ct)
    {
        var repo = await Resolve(request, ct).ConfigureAwait(false);

        if (Missing(request, ToolArguments.Repository, ToolArguments.DefaultBranch) is { } error)
        {
            return error;
        }

        if (repo is null)
        {
            return NoRepository(request);
        }

        var branch = ToolArguments.Text(request, ToolArguments.DefaultBranch);

        var set = await _defaults.HandleAsync(repo.Path, branch, ct).ConfigureAwait(false);

        return From(set, $"{repo.Name} now defaults to {branch}.");
    }

    private async Task<McpResult> DistributeSecrets(McpRequest request, CancellationToken ct)
    {
        var repo = await Resolve(request, ct).ConfigureAwait(false);

        if (repo is null)
        {
            return Missing(request, ToolArguments.Repository) ?? NoRepository(request);
        }

        var plan = _secrets.Plan(root, repo.Name, repo.Path, repo.DefaultBranch);

        if (plan.Files.Count == 0)
        {
            return Ok($"{repo.Name} has no secret files to copy.");
        }

        var seeded = _secrets.Distribute(plan);

        return Ok($"copied {plan.Files.Count} secret file(s) into {seeded} worktree(s) of {repo.Name}.");
    }

    private async Task<McpResult> Dispatch(McpRequest request, CancellationToken ct)
    {
        if (Missing(request, ToolArguments.Message) is { } error)
        {
            return error;
        }

        var dispatcher = new DispatchHandler(
            mux, store, harnessConfig, namer: Adapters.SlugNamer(), settings: Adapters.Settings(),
            intents: Adapters.Intents(), starter: this);

        var reply = await dispatcher
            .HandleAsync(
                new DispatchCommand(
                    project,
                    root,
                    ToolArguments.Text(request, ToolArguments.Message),
                    caller,
                    ToolArguments.Text(request, ToolArguments.Profile),
                    Repo(request),
                    Branch(request),
                    ToolArguments.Flag(request, ToolArguments.Research)),
                DateTimeOffset.UtcNow.ToString("O"))
            .ConfigureAwait(false);

        return reply.Succeeded ? Ok(reply.Value!.Note) : McpResult.Error(reply.Error!);
    }

    private async Task<McpResult> ListSubs(CancellationToken ct)
    {
        var paneOpen = await ProjectStructureReader.PaneOpenAsync(mux, ct).ConfigureAwait(false);

        return Ok(SubSummary.Text(_agents.Handle(project), paneOpen));
    }

    private async Task<McpResult> StopSub(McpRequest request, CancellationToken ct)
    {
        if (Missing(request, ToolArguments.Slug) is { } error)
        {
            return error;
        }

        var slug = ToolArguments.Text(request, ToolArguments.Slug);
        var sub = RemoveSubHandler.Find(_agents.Handle(project), slug);

        if (sub is null)
        {
            return McpResult.Error(RemoveSubHandler.NotFound(slug));
        }

        var outcome = await _stopper.HandleAsync(project, sub, ct).ConfigureAwait(false);

        return outcome.Succeeded
            ? Ok($"stopped {sub.Branch}; its record and folder are kept.")
            : McpResult.Error($"{sub.Branch} is not running.");
    }

    private async Task<McpResult> RemoveSub(McpRequest request, CancellationToken ct)
    {
        if (Missing(request, ToolArguments.Slug) is { } error)
        {
            return error;
        }

        var removed = await _subRemover
            .HandleAsync(
                new RemoveSubCommand(
                    project,
                    ToolArguments.Text(request, ToolArguments.Slug),
                    caller,
                    ToolArguments.Flag(request, ToolArguments.DeleteFolder),
                    ToolArguments.Flag(request, ToolArguments.RemoveAgents)),
                ct)
            .ConfigureAwait(false);

        return removed.Succeeded ? Ok(removed.Value!.Note) : McpResult.Error(removed.Error!);
    }

    private async Task<McpResult> Report(McpRequest request, CancellationToken ct)
    {
        var status = ToolArguments.Text(request, ToolArguments.Status);

        var reported = await _reporter
            .HandleAsync(project, root, caller, status, ToolArguments.Text(request, ToolArguments.Summary), ct)
            .ConfigureAwait(false);

        if (reported.Succeeded
            && OrchestrationStatus.Normalize(status)
                is OrchestrationStatus.Done or OrchestrationStatus.Failed)
        {
            Adapters.Notifier().Notify(
                $"{caller}: {OrchestrationStatus.Normalize(status)}");
        }

        return reported.Succeeded ? Ok(reported.Value!) : McpResult.Error(reported.Error!);
    }

    private Task<RepositorySummary?> Resolve(McpRequest request, CancellationToken ct) =>
        Named(Repo(request), ct);

    private async Task<RepositorySummary?> Named(string name, CancellationToken ct)
    {
        if (name.Length == 0)
        {
            return null;
        }

        var summaries = await _repos.HandleAsync(root, ct).ConfigureAwait(false);

        return summaries.FirstOrDefault(
            s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private AgentRecord? Find(McpRequest request) =>
        AgentKey.Find(_agents.Handle(project), Repo(request), Branch(request));

    private static string Repo(McpRequest request) => ToolArguments.Text(request, ToolArguments.Repository);

    private static string Branch(McpRequest request) => ToolArguments.Text(request, ToolArguments.Branch);

    private McpResult NoRepository(McpRequest request) =>
        McpResult.Error($"No repository named '{Repo(request)}' in this project.");

    private static McpResult? Missing(McpRequest request, params string[] required)
    {
        var missing = ToolArguments.Missing(request, required);

        return missing is null ? null : McpResult.Error(missing);
    }

    private static McpResult From(Result result, string ok) =>
        result.Succeeded ? McpResult.Ok(ok) : McpResult.Error(result.Error!);

    private static McpResult Ok(string text) => McpResult.Ok(text);
}
