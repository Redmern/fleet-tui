using System.Globalization;
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
using Fleet.Features.Orchestrations.Flags;
using Fleet.Features.Orchestrations.ListSubs;
using Fleet.Features.Orchestrations.ReportStatus;
using Fleet.Features.Orchestrations.ReportStatus.Models;
using Fleet.Features.Orchestrations.SetProfile;
using Fleet.Features.Orchestrations.WaitForAgent;
using Fleet.Features.Repositories.AddRepository;
using Fleet.Features.Repositories.AddRepository.Models;
using Fleet.Features.Repositories.ListRepositories;
using Fleet.Features.Repositories.ListRepositories.Models;
using Fleet.Features.Repositories.OpenPr;
using Fleet.Features.Repositories.OpenPr.Models;
using Fleet.Features.Repositories.PullRepository;
using Fleet.Features.Repositories.RemoveRepository;
using Fleet.Features.Repositories.Secrets;
using Fleet.Features.Repositories.SetDefaultBranch;
using Fleet.Features.Todos.AddTodo;
using Fleet.Ports;
using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;
using Fleet.Ports.Forge;
using Fleet.Ports.Git;
using Fleet.Ports.Harness;
using Fleet.Ports.Mcp.Models;
using Fleet.Ports.Mux;
using Fleet.Ports.Mux.Enums;
using Fleet.Ports.Mux.Models;
using Fleet.Ports.Orchestrations;
using Fleet.Shared;
using Fleet.Shared.Constants;
using Fleet.Shared.Evidence;
using Fleet.Shared.Harness;
using Fleet.Shared.Harness.Enums;
using Fleet.Shared.Mcp;
using Fleet.Shared.Messaging;
using Fleet.Shared.Orchestrations;
using Fleet.Shared.Results;
using Fleet.Shared.Settings;
using Fleet.Shared.Settings.Enums;
using Fleet.Shared.Settings.Models;
using Fleet.Shared.Status;
using Fleet.Shared.Todos;

namespace Fleet.Cli.Composition;

public sealed class McpActions(
    string project,
    string root,
    string caller,
    IGitRunner git,
    IMuxDriver mux,
    IAgentStore store,
    IHarnessConfigs claudeConfigs,
    IHarnessConfigs nonClaudeConfigs,
    ISlugNamer namer,
    TimeProvider clock,
    Action<string> trustFolder,
    IFleetLog log,
    IForgeProbe? forgeProbe = null,
    IProcessRunner? processes = null) : IAgentStarter
{
    private readonly OpenPrHandler _prOpener = new(
        git, processes ?? McpWiring.Processes(), forgeProbe ?? McpWiring.ForgeProbe());

    private readonly ListAgentsHandler _agents = new(store);

    private readonly CallerHarnesses _callers = new(project, caller, store);

    private readonly ListRepositoriesHandler _repos = new(git);

    private readonly ListBranchesHandler _branches = new(git);

    private readonly NewAgentHandler _spawner = new(
        git, mux, store, Adapters.Models, Adapters.SubagentGuidance, Adapters.ModelRouter(), log,
        configs: nonClaudeConfigs);

    private readonly HideAgentHandler _hider = new(mux, store);

    private readonly StopAgentHandler _stopper = new(mux, store, Adapters.AgentStates(), log);

    private readonly RemoveAgentHandler _remover = new(git, mux, store, hookState: Adapters.AgentStates());

    private readonly RemoveSubHandler _subRemover = new(
        new RemoveAgentHandler(git, mux, store, hookState: Adapters.AgentStates()),
        store,
        mux,
        (project, sub, ct) => new StopAgentHandler(mux, store, Adapters.AgentStates(), log).HandleAsync(project, sub, ct));

    private readonly ChangeHarnessHandler _harnesses = new(store);

    private readonly AddRepositoryHandler _adder = new(git);

    private readonly PullRepositoryHandler _puller = new(git);

    private readonly DefaultBranchLag _defaultLag = new(git);

    private readonly RemoveRepositoryHandler _repoRemover = new(git);

    private readonly SetDefaultBranchHandler _defaults = new(git);

    private readonly SecretsHandler _secrets = new();

    private const string MainHarness = CallerHarnesses.MainHarness;

    private static readonly TimeSpan FirstTaskEchoWithin = TimeSpan.FromSeconds(60);

    private static readonly TimeSpan AnswerMargin = TimeSpan.FromSeconds(10);

    private readonly TellAgentHandler _teller = new(mux, inboxes: Adapters.AgentInboxes(), clock: clock);

    private readonly BranchStates _states = new(git);

    private readonly ForwardPortsHandler _forwardPorts = new(Adapters.Forwards(), Adapters.Browser(), Adapters.KnownRemotes(), Environment.UserName);

    public async Task<McpResult> PerformAsync(McpRequest request, CancellationToken ct)
    {
        var tool = HarnessToolIds.Parse(request.Tool);

        return tool switch
        {
            HarnessTool.ListAgents => await ListAgents(ct).ConfigureAwait(false),
            HarnessTool.ListRepositories => await ListRepositories(ct).ConfigureAwait(false),
            HarnessTool.ListBranches => await ListBranches(request, ct).ConfigureAwait(false),
            HarnessTool.AgentStatus => await AgentStatus(request, ct).ConfigureAwait(false),
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
            HarnessTool.OpenPr => await OpenPr(request, ct).ConfigureAwait(false),
            HarnessTool.RemoveRepository => await RemoveRepository(request, ct).ConfigureAwait(false),
            HarnessTool.SetDefaultBranch => await SetDefaultBranch(request, ct).ConfigureAwait(false),
            HarnessTool.DistributeSecrets => await DistributeSecrets(request, ct).ConfigureAwait(false),
            HarnessTool.Dispatch => await Dispatch(request, ct).ConfigureAwait(false),
            HarnessTool.ListSubs => await ListSubs(ct).ConfigureAwait(false),
            HarnessTool.StopSub => await StopSub(request, ct).ConfigureAwait(false),
            HarnessTool.RemoveSub => await RemoveSub(request, ct).ConfigureAwait(false),
            HarnessTool.Report => await Report(request, ct).ConfigureAwait(false),
            HarnessTool.WaitForAgent => await WaitForAgent(request, ct).ConfigureAwait(false),
            HarnessTool.ListForwards => await Forward(request, ForwardVerb.List, ct).ConfigureAwait(false),
            HarnessTool.ForwardPort => await Forward(request, ForwardVerb.Add, ct).ConfigureAwait(false),
            HarnessTool.UnforwardPort => await Forward(request, ForwardVerb.Remove, ct).ConfigureAwait(false),
            HarnessTool.OpenUrl => await Forward(request, ForwardVerb.Open, ct).ConfigureAwait(false),
            HarnessTool.StartStack => await Forward(request, ForwardVerb.Start, ct).ConfigureAwait(false),
            HarnessTool.StopStack => await Forward(request, ForwardVerb.Stop, ct).ConfigureAwait(false),
            HarnessTool.TodoAdd => TodoAdd(request),
            HarnessTool.SetProfile => SetProfile(request),
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

    private async Task<McpResult> ListAgents(CancellationToken ct)
    {
        var agents = await WithQuietPanes(_agents.Handle(project), ct).ConfigureAwait(false);
        var drifts = new Dictionary<string, ReportDrift>(StringComparer.Ordinal);
        var probe = new ReportDriftProbe(git);

        foreach (var agent in agents.Where(a => !AgentHarness.IsOrchestrator(a.Harness)))
        {
            var head = ReportDrift.HeadToCheck(agent.Status, agent.Evidence);

            if (head.Length > 0 && await probe.MeasureAsync(agent.Worktree, head, ct).ConfigureAwait(false) is { } drift)
            {
                drifts[agent.Worktree] = drift;
            }
        }

        return Ok(ToolText.Agents(agents, a => drifts.GetValueOrDefault(a.Worktree)));
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

    private async Task<McpResult> AgentStatus(McpRequest request, CancellationToken ct)
    {
        if (Missing(request, ToolArguments.Repository, ToolArguments.Branch) is { } error)
        {
            return error;
        }

        var agent = Find(request);

        if (agent is null)
        {
            return McpResult.Error(ToolText.NotFound(Repo(request), Branch(request)));
        }

        var state = _states.For(agent.Worktree, agent.BaseRef, ReportDrift.HeadToCheck(agent.Status, agent.Evidence));
        var against = await DistanceFromBaseAsync(agent, ct).ConfigureAwait(false);

        return Ok(against is { } measured
            ? ToolText.Agent(agent, new BranchState(measured.Ahead, measured.Behind, state.Dirty, state.AfterReport))
            : ToolText.Agent(agent, state, baseMeasured: false));
    }

    private async Task<(int Ahead, int Behind)?> DistanceFromBaseAsync(AgentRecord agent, CancellationToken ct)
    {
        if (agent.BaseRef.Trim().Length == 0 || !Directory.Exists(agent.Worktree))
        {
            return null;
        }

        var counts = await git
            .RunAsync(agent.Worktree, ["rev-list", "--left-right", "--count", $"{agent.BaseRef}...HEAD"], null, ct)
            .ConfigureAwait(false);

        if (!counts.Ok)
        {
            return null;
        }

        var (behind, ahead) = Tally.Parse(counts.Out);

        return (ahead, behind);
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
        var lag = await _defaultLag.DescribeAsync(repo.Path, repo.DefaultBranch, ct).ConfigureAwait(false);

        return Ok(
            $"{repo.Name} (default {repo.DefaultBranch}): {worktrees} worktree(s), "
            + $"{unpushed} branch(es) not pushed; {lag}.");
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

        var harness = ToolArguments.Text(request, ToolArguments.Harness).Trim();

        if (harness.Length > 0 && HarnessRegistry.Require(harness) is { Succeeded: false } refused)
        {
            return McpResult.Error(refused.Error!);
        }

        var started = await Start(
                repo, Branch(request), caller, ToolArguments.Text(request, ToolArguments.Task), ct, harness,
                DeadlineOf(request), ToolArguments.Text(request, ToolArguments.Base))
            .ConfigureAwait(false);

        return started.Succeeded ? Ok(started.Value!) : McpResult.Error(started.Error!);
    }

    public async Task<Result<string>> StartAsync(AgentStart request, CancellationToken ct = default)
    {
        var repo = await Named(request.Repository, ct).ConfigureAwait(false);

        return repo is null
            ? Result<string>.Fail($"No repository named '{request.Repository}' in this project.")
            : await Start(repo, request.Branch, caller, request.Task, ct, request.Harness, request.By, request.Base)
                .ConfigureAwait(false);
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
        RepositorySummary repo, string branch, string owner, string task, CancellationToken ct, string harness = "",
        DateTimeOffset? by = null, string from = "")
    {
        var deadline = by ?? clock.GetUtcNow() + CallBudget();
        var (chosen, hostInNvim) = AgentHarness.RunningAi(
            harness.Length > 0 ? harness : Adapters.Settings().Load(project).DefaultAgentHarness);
        var spawned = await _spawner
            .StartAsync(
                new NewAgentCommand(
                    project, repo.Name, repo.Path, branch, from.Length > 0 ? from : repo.DefaultBranch,
                    chosen, owner, HostInNvim: hostInNvim, Task: task, ExactBase: from.Length > 0),
                ct)
            .ConfigureAwait(false);

        if (!spawned.Succeeded)
        {
            return Result<string>.Fail(spawned.Error!);
        }

        var agent = spawned.Value.Agent;

        if (owner.Trim().Length > 0)
        {
            agent = agent with { Owner = owner };
            store.Save(project, agent);
            await WarnStartedBeforeSkeletonAsync(agent, ct).ConfigureAwait(false);
            await CheckOverlapsAsync(owner, ct).ConfigureAwait(false);
        }

        claudeConfigs.For(agent.Harness)
            .WriteForAgent(agent.Worktree, project, repo.Name, branch);

        var name = SessionNames.RepoAgent(project, agent.Repository, agent.Branch);
        var runs = TellAgentHandler.HarnessOf(agent);
        var session = HarnessRoute.Native(CallerHarness(), runs)
            ? $" Its Claude session is named \"{name}\".{BaseNote.For(spawned.Value, from)}"
            : $" It runs {runs}: tell_agent types into its pane, and wait_for_agent waits for it to finish."
              + BaseNote.For(spawned.Value, from);

        if (task.Length == 0)
        {
            return Result<string>.Ok($"started {repo.Name}/{branch}.{session}");
        }

        var delivered = await SendWhenReady(agent, task, deadline, ct).ConfigureAwait(false);

        return Result<string>.Ok(delivered switch
        {
            null => $"started {repo.Name}/{branch}, but it was not ready to take the task; "
                + $"use tell_agent once it is up.{session}",
            { } done when DispatchNote.Confirmed(done) =>
                $"started {repo.Name}/{branch} and gave it its first task.{session}",
            { } doubted =>
                $"started {repo.Name}/{branch}; its first task is written to .fleet/instruction.md but delivery was "
                + $"not confirmed ({DispatchNote.Reason(doubted)}); use tell_agent to check on it.{session}",
        });
    }

    private DateTimeOffset DeadlineOf(McpRequest request) => (request.Arrived ?? clock.GetUtcNow()) + CallBudget();

    private TimeSpan CallBudget() =>
        (HarnessRegistry.Find(CallerHarness())?.ClientToolTimeout
            ?? HarnessRegistry.Profiles.Select(p => p.ClientToolTimeout).OfType<TimeSpan>().Min())
        - AnswerMargin;

    private TimeSpan Remaining(DateTimeOffset deadline) =>
        TimeSpan.FromTicks(Math.Max(0, (deadline - clock.GetUtcNow()).Ticks));

    private async Task<PromptDelivery?> SendWhenReady(
        AgentRecord agent, string message, DateTimeOffset deadline, CancellationToken ct)
    {
        var marker = OrchestrationPaths.ReadyMarker(agent.Worktree);

        for (var i = 0; i < 80 && !File.Exists(marker) && Remaining(deadline) > TimeSpan.Zero; i++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(300), clock, ct).ConfigureAwait(false);
        }

        if (!File.Exists(marker))
        {
            return null;
        }

        var pane = await PaneFor(agent, ct).ConfigureAwait(false);

        if (pane is null)
        {
            return null;
        }

        var echoWithin = Min(FirstTaskEchoWithin, Remaining(deadline));
        var delivery = await _teller
            .DeliverAsync(agent, pane.Value, message, ct, echoWithin > TimeSpan.Zero ? echoWithin : TimeSpan.FromMilliseconds(1))
            .ConfigureAwait(false);

        if (delivery == PromptDelivery.Unchecked)
        {
            return PromptDelivery.Echoed;
        }

        if (delivery != PromptDelivery.Pumped)
        {
            return delivery;
        }

        var outcome = await PumpMarkers
            .SeenAsync(agent.Worktree, Min(FirstTaskEchoWithin, Remaining(deadline)), TimeSpan.FromMilliseconds(300), clock, ct)
            .ConfigureAwait(false);

        return outcome switch
        {
            PumpOutcome.Seen => PromptDelivery.Pumped,
            PumpOutcome.Unconfirmed => PromptDelivery.PumpUnconfirmed,
            _ => PromptDelivery.PumpUnseen,
        };
    }

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;

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
        var stamped = MarkInstructed(agent);
        string? address;

        try
        {
            address = await _teller
                .RouteAsync(agent, pane.Value, message, ToolArguments.Flag(request, ToolArguments.Typed), CallerHarness(), ct)
                .ConfigureAwait(false);
        }
        catch
        {
            if (stamped is not null)
            {
                UndoInstructed(agent, stamped);
            }

            throw;
        }

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

    private sealed record Stamped(string At, string PreviousAt, string PreviousStatus, string ReportedAt);

    private Stamped? MarkInstructed(AgentRecord agent)
    {
        var at = clock.GetUtcNow().ToString("O", CultureInfo.InvariantCulture);
        var previousAt = string.Empty;
        var previousStatus = string.Empty;
        var reportedAt = string.Empty;

        var kept = store.Update(project, agent.Worktree, current =>
        {
            previousAt = current.InstructedAt;
            previousStatus = current.Status;
            reportedAt = current.ReportedAt;

            return current with
            {
                InstructedAt = at,
                Status = OrchestrationStatus.Normalize(current.Status) is OrchestrationStatus.Done or OrchestrationStatus.Failed
                    ? OrchestrationStatus.Working
                    : current.Status,
            };
        });

        if (!kept)
        {
            log.Write(LogTag.For(
                project, $"tell_agent could not record the instruction time of {agent.Repository}/{agent.Branch}"));
        }

        return kept ? new Stamped(at, previousAt, previousStatus, reportedAt) : null;
    }

    private void UndoInstructed(AgentRecord agent, Stamped stamped)
    {
        var undone = store.Update(project, agent.Worktree, current =>
            current.InstructedAt != stamped.At
                ? current
                : current with
                {
                    InstructedAt = stamped.PreviousAt,
                    Status = current.Status == OrchestrationStatus.Working && current.ReportedAt == stamped.ReportedAt
                        ? stamped.PreviousStatus
                        : current.Status,
                });

        if (!undone)
        {
            log.Write(LogTag.For(
                project, $"tell_agent could not put back the record of {agent.Repository}/{agent.Branch}"));
        }
    }

    private async Task WarnStartedBeforeSkeletonAsync(AgentRecord agent, CancellationToken ct)
    {
        try
        {
            await WarnIfSkeletonOpenAsync(agent, ct).ConfigureAwait(false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NullReferenceException)
        {
            log.Write(LogTag.For(project, $"skeleton gate check for {agent.Repository}/{agent.Branch} failed: {e.Message}"));
        }
    }

    private async Task WarnIfSkeletonOpenAsync(AgentRecord agent, CancellationToken ct)
    {
        var owner = store.List(project).FirstOrDefault(a =>
            AgentHarness.IsOrchestrator(a.Harness) && string.Equals(a.Branch, agent.Owner.Trim(), StringComparison.OrdinalIgnoreCase));

        if (owner is not null
            && SkeletonGate.EarlyStart(Adapters.Intents().Load(owner.Worktree), agent.Repository, agent.Branch) is { } line)
        {
            await Owners().AboutAsync(agent, line, ct).ConfigureAwait(false);
        }
    }

    private AgentRecord? CallerAgent() =>
        store.List(project).FirstOrDefault(a => !AgentHarness.IsOrchestrator(a.Harness)
            && string.Equals(McpCaller.ForAgent(a.Repository, a.Branch), caller, StringComparison.OrdinalIgnoreCase));

    private async Task CheckOverlapsAsync(string owner, CancellationToken ct)
    {
        try
        {
            await Owners().Overlaps(git).CheckAsync(project, owner, ct).ConfigureAwait(false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log.Write(LogTag.For(project, $"overlap check for {owner} failed: {e.Message}"));
        }
    }

    private OwnerNotices Owners() =>
        new(project, root, mux, store, log, () => Adapters.Settings().Load(project).MainOrchestratorInNvim, clock);

    private string CallerHarness() => _callers.Current();

    private async Task<IReadOnlyList<AgentRecord>> WithQuietPanes(IReadOnlyList<AgentRecord> agents, CancellationToken ct)
    {
        if (!agents.Any(AgentActivity.WatchedByPane))
        {
            return agents;
        }

        var panes = await mux.ListPanesAsync(ct).ConfigureAwait(false);
        var settings = Adapters.Settings().Load(project);
        var shown = new List<AgentRecord>(agents.Count);

        foreach (var agent in agents)
        {
            shown.Add(await WithQuietPane(agent, panes, settings, ct).ConfigureAwait(false));
        }

        return shown;
    }

    private async Task<AgentRecord> WithQuietPane(
        AgentRecord agent, IReadOnlyList<Pane> panes, SettingsConfig settings, CancellationToken ct)
    {
        var pane = panes.FirstOrDefault(p => AgentPanes.Owns(p, agent) && !SubBrowse.Is(p));

        if (!AgentActivity.WatchedByPane(agent) || pane is null)
        {
            return agent;
        }

        var text = await mux.GetTextAsync(pane.Id, ct).ConfigureAwait(false);
        var live = AgentActivity.Quiet(
            agent.Worktree, text, clock.GetUtcNow().UtcDateTime, PaneIdleRule.For(settings, agent.RunningHarness));
        var finished = OrchestrationStatus.Normalize(agent.Status)
            is OrchestrationStatus.Done or OrchestrationStatus.Failed;

        return live.Length == 0 || (finished && live == AgentActivity.Idle) ? agent : agent with { Status = live };
    }

    private async Task<string?> PaneTextFor(AgentRecord agent, CancellationToken ct)
    {
        var pane = await PaneFor(agent, ct).ConfigureAwait(false);

        return pane is null ? null : await mux.GetTextAsync(pane.Value, ct).ConfigureAwait(false);
    }

    private async Task<McpResult> WaitForAgent(McpRequest request, CancellationToken ct)
    {
        if (Missing(request, ToolArguments.Branch) is { } error)
        {
            return error;
        }

        var waited = await new WaitForAgentHandler(
                store, PaneTextFor, () => clock.GetUtcNow().UtcDateTime, (span, token) => Task.Delay(span, clock, token),
                settings: () => Adapters.Settings().Load(project))
            .HandleAsync(
                project,
                Repo(request),
                Branch(request),
                WaitForAgentHandler.TimeoutFrom(ToolArguments.Count(request, ToolArguments.Timeout, 0), CallerHarness()),
                ct)
            .ConfigureAwait(false);

        return waited.Succeeded ? Ok(waited.Value!) : McpResult.Error(waited.Error!);
    }

    private async Task<PaneId?> PaneFor(AgentRecord agent, CancellationToken ct)
    {
        var panes = await mux.ListPanesAsync(ct).ConfigureAwait(false);
        var pane = panes.FirstOrDefault(p => AgentPanes.Owns(p, agent) && !SubBrowse.Is(p));

        return pane?.Id;
    }

    private bool SubOrchestratorsHostedInNvim() => Adapters.SubOrchestratorsHostedInNvim(project);

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

        trustFolder(agent.Worktree);

        var outcome = await new OpenAgentHandler(
                mux, store, SubOrchestratorsHostedInNvim(), models: Adapters.Models(project), agentsInNvim: Adapters.AgentsInNvim(project),
                configs: nonClaudeConfigs, statusHooks: Adapters.StatusHooks(project))
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

        var outcome = await _remover.RemoveAsync(project, agent, delete, ct).ConfigureAwait(false);

        if (!outcome.Forgotten)
        {
            return McpResult.Error(outcome.Problem!);
        }

        if (outcome.Problem is not null)
        {
            return Ok($"removed {Repo(request)}/{Branch(request)}. {outcome.Problem}");
        }

        return Ok(delete
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
                + $"{AgentHarness.Describe(changed.Value!.Harness, changed.Value.HostInNvim)}.")
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

    private async Task<McpResult> OpenPr(McpRequest request, CancellationToken ct)
    {
        if (Missing(
                request, ToolArguments.Repository, ToolArguments.Branch, ToolArguments.Title, ToolArguments.BodyFile)
            is { } error)
        {
            return error;
        }

        var settings = Adapters.Settings().Load(project);

        if (SyncIso.Refused(project, Adapters.Iso().Load().On, settings) is { } isolated)
        {
            return McpResult.Error(isolated);
        }

        var repo = await Resolve(request, ct).ConfigureAwait(false);

        if (repo is null)
        {
            return NoRepository(request);
        }

        var @base = ToolArguments.Text(request, ToolArguments.Base);

        var opened = await _prOpener
            .HandleAsync(
                new OpenPrCommand(
                    repo.Name,
                    repo.Path,
                    Branch(request),
                    @base.Length > 0 ? @base : repo.DefaultBranch,
                    ToolArguments.Text(request, ToolArguments.Title),
                    ToolArguments.Text(request, ToolArguments.BodyFile),
                    settings.RepositoryFor(repo.Name),
                    root),
                ct)
            .ConfigureAwait(false);

        return opened.Succeeded ? Ok(opened.Value) : McpResult.Error(opened.Error!);
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
            mux, store, claudeConfigs.For(HarnessRegistry.ClaudeName), namer: namer, settings: Adapters.Settings(),
            intents: Adapters.Intents(), starter: this, router: Adapters.ModelRouter(), log: log,
            configs: nonClaudeConfigs, clock: clock);

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
                    ToolArguments.Flag(request, ToolArguments.Research),
                    ToolArguments.Text(request, ToolArguments.Harness),
                    Remaining(DeadlineOf(request)),
                    ToolArguments.Text(request, ToolArguments.Base)),
                clock.GetUtcNow().ToString("O"),
                ct)
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
                    ToolArguments.Flag(request, ToolArguments.RemoveAgents),
                    ToolArguments.Flag(request, ToolArguments.Stop)),
                ct)
            .ConfigureAwait(false);

        return removed.Succeeded ? Ok(removed.Value!.Note) : McpResult.Error(removed.Error!);
    }

    private async Task<McpResult> Report(McpRequest request, CancellationToken ct)
    {
        var status = ToolArguments.Text(request, ToolArguments.Status);

        var reporter = new ReportStatusHandler(
            store,
            clock.GetUtcNow,
            inboxes: Adapters.AgentInboxes(),
            tickets: Adapters.TicketStatus(),
            typeToOwner: Owners().TypeAsync,
            mainHarness: MainHarness,
            git: git);

        var reported = await reporter
            .HandleAsync(
                project,
                root,
                caller,
                status,
                ToolArguments.Text(request, ToolArguments.Summary),
                ct,
                VerifyFrom(request))
            .ConfigureAwait(false);

        if (reported.Succeeded && CallerAgent() is { } reportedBy)
        {
            await CheckOverlapsAsync(reportedBy.Owner, ct).ConfigureAwait(false);
        }

        return reported.Succeeded ? Ok(reported.Value!) : McpResult.Error(reported.Error!);
    }

    private static VerifyReport? VerifyFrom(McpRequest request)
    {
        var verify = new VerifyReport(
            ToolArguments.Text(request, ToolArguments.VerifyCommand),
            ToolArguments.Integer(request, ToolArguments.VerifyExitCode),
            request.Value(ToolArguments.VerifyOutput));

        return verify.Given ? verify : null;
    }

    private McpResult TodoAdd(McpRequest request)
    {
        if (Missing(request, ToolArguments.Title) is { } error)
        {
            return error;
        }

        var added = new AddTodoHandler(Adapters.Todos()).HandleTool(
            root,
            ToolArguments.Text(request, ToolArguments.Title),
            ToolArguments.Text(request, ToolArguments.Body),
            ToolArguments.Text(request, ToolArguments.Effort),
            clock.GetUtcNow());

        if (!added.Succeeded)
        {
            return McpResult.Error(added.Error!);
        }

        log.Write(LogTag.For(project, $"{caller} added todo {added.Value.Id}"));

        return Ok($"added todo {added.Value.Id} ({TodoPaths.File(root, added.Value.Id)}).");
    }

    private McpResult SetProfile(McpRequest request)
    {
        var set = new SetProfileHandler(store, Adapters.Intents(), Adapters.Settings()).Handle(
            project, root, caller, ToolArguments.Text(request, ToolArguments.Profile), clock.GetUtcNow().ToString("O"));

        return set.Succeeded ? Ok(set.Value!) : McpResult.Error(set.Error!);
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
