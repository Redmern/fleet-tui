using Fleet.Features.Agents.NewAgent.Models;
using Fleet.Ports;
using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;
using Fleet.Ports.Git;
using Fleet.Ports.Harness;
using Fleet.Ports.Mux;
using Fleet.Ports.Mux.Models;
using Fleet.Ports.Orchestrations;
using Fleet.Ports.Settings;
using Fleet.Shared;
using Fleet.Shared.Constants;
using Fleet.Shared.Orchestrations;
using Fleet.Shared.Results;
using Fleet.Shared.Settings;
using Fleet.Shared.Settings.Models;

namespace Fleet.Features.Agents.NewAgent;

public sealed class NewAgentHandler(
    IGitRunner git,
    IMuxDriver mux,
    IAgentStore store,
    Func<string, RoleModels>? models = null,
    Func<string, bool>? subagentGuidance = null,
    IModelRouter? router = null,
    IFleetLog? log = null,
    ISettingsStore? settings = null,
    IHarnessConfigs? configs = null)
{
    public async Task<Result<AgentRecord>> HandleAsync(
        NewAgentCommand command, CancellationToken ct = default)
    {
        var started = await StartAsync(command, ct).ConfigureAwait(false);

        return started.Succeeded
            ? Result<AgentRecord>.Ok(started.Value.Agent)
            : Result<AgentRecord>.Fail(started.Error!);
    }

    public async Task<Result<StartedAgent>> StartAsync(
        NewAgentCommand command, CancellationToken ct = default)
    {
        var (harness, hostInNvim) = AgentHarness.Hosted(
            command.Harness, command.HostInNvim, settings?.Load(command.ProjectName).HostsInNvim ?? true);
        command = command with { Harness = harness, HostInNvim = hostInNvim };

        var planned = AgentBranch.Plan(command.BranchName, command.Base);

        if (!planned.Succeeded)
        {
            return Fail(planned.Error!);
        }

        var branch = planned.Value!.Branch;

        if (!Directory.Exists(command.RepositoryDirectory))
        {
            return Fail($"{command.RepositoryDirectory} does not exist.");
        }

        var bare = await git
            .RunAsync(command.RepositoryDirectory, ["rev-parse", "--is-bare-repository"], null, ct)
            .ConfigureAwait(false);

        var plan = WorktreePlanner.For(
            command.RepositoryDirectory,
            branch,
            bare.Ok && bare.Out == "true",
            WorktreePlanner.LooksLikeWorktree);

        var baseRef = branch;
        BranchStart start;

        if (plan.MustCreate)
        {
            var resolved = await ResolveBaseRefAsync(plan.Anchor, planned.Value!.Base, command.ExactBase, ct)
                .ConfigureAwait(false);

            if (!resolved.Succeeded)
            {
                return Fail(resolved.Error!);
            }

            baseRef = resolved.Value!;

            var created = await AddWorktreeAsync(plan, branch, baseRef, ct).ConfigureAwait(false);

            if (!created.Succeeded)
            {
                return Fail(created.Error!);
            }

            start = created.Value;

            await SeedSecretsAsync(command, plan.TargetDirectory, ct).ConfigureAwait(false);
        }
        else
        {
            start = new BranchStart(await TipAsync(plan.TargetDirectory, "HEAD", ct).ConfigureAwait(false));
        }

        var roles = models?.Invoke(command.ProjectName) ?? SettingsDefaults.Models;
        var routed = roles.Agent.IsAuto && AgentHarness.RunsHarness(command.Harness)
            ? await RoutedModel(command, branch, ct).ConfigureAwait(false)
            : null;
        var stored = store.List(command.ProjectName)
            .FirstOrDefault(a => string.Equals(a.Worktree, plan.TargetDirectory, StringComparison.Ordinal))
            ?.Model;

        var agent = new AgentRecord(
            plan.TargetDirectory,
            command.RepositoryName,
            branch,
            command.Harness,
            baseRef,
            plan.TargetDirectory != command.RepositoryDirectory,
            Hidden: true,
            Open: true,
            Owner: command.Owner,
            HostInNvim: command.HostInNvim,
            Model: routed ?? stored);

        store.Save(command.ProjectName, agent);

        SubagentGuidance.Apply(
            agent.Worktree,
            agent.RunsHarness && subagentGuidance?.Invoke(command.ProjectName) == true);

        var args = agent.Command(
            ClaudeLaunch.ForAgent(
                command.ProjectName,
                agent.Repository,
                agent.Branch,
                orchestrator: false,
                agent.ModelsFrom(roles, orchestrator: false),
                agent.Worktree));

        configs?.For(agent.Harness).WriteForAgent(agent.Worktree, command.ProjectName, agent.Repository, agent.Branch);

        var active = (await mux.ListPanesAsync(ct).ConfigureAwait(false)).FirstOrDefault(p => p.IsActive);

        AgentTraces.Opened(agent, settings?.Load(command.ProjectName).StatusHooks ?? true);

        var pane = await mux.SpawnAsync(
            HiddenSpawn.Into(
                mux,
                command.ProjectName,
                new SpawnOptions
                {
                    Cwd = plan.TargetDirectory,
                    Args = args,
                    Env = AgentHarness.SpawnEnvFor(args),
                }),
            ct).ConfigureAwait(false);

        if (pane.IsNone)
        {
            return Fail($"the {mux.Name} multiplexer did not respond. Run 'fleet doctor'.");
        }

        await mux.SetTitleAsync(pane, AgentTitle.For(agent.Repository, agent.Branch), ct)
            .ConfigureAwait(false);

        if (active is not null)
        {
            await mux.FocusPaneAsync(active.Id, ct).ConfigureAwait(false);
        }

        Remember(command);

        return Result<StartedAgent>.Ok(new StartedAgent(agent, start.ExistingTip, start.Tracks));
    }

    private void Remember(NewAgentCommand command)
    {
        if (settings is null)
        {
            return;
        }

        settings.Save(
            command.ProjectName,
            settings.Load(command.ProjectName).WithLastNewAgent(command.RepositoryName, Remembered(command)));
    }

    public static string Remembered(NewAgentCommand command) =>
        command.HostInNvim ? AgentHarness.Nvim : command.Harness;

    private async Task<RoleModel?> RoutedModel(NewAgentCommand command, string branch, CancellationToken ct)
    {
        var task = command.Task.Trim();

        if (task.Length == 0)
        {
            return null;
        }

        var routed = await router
            .TryChooseAsync(new ModelRoutingRequest(task, null, false, command.RepositoryName), log, ct)
            .ConfigureAwait(false);

        var outcome = ModelRouting.OrFallback(
            routed, SettingsDefaults.AgentModel, $"agent {command.RepositoryName}/{branch}");

        if (outcome.LogLine is not null)
        {
            log?.Write(outcome.LogLine);
            return null;
        }

        return outcome.Model;
    }

    private async Task SeedSecretsAsync(
        NewAgentCommand command, string worktree, CancellationToken ct)
    {
        var projectRoot = Directory.GetParent(command.RepositoryDirectory)?.FullName;

        if (projectRoot is null)
        {
            return;
        }

        var head = await git
            .RunAsync(command.RepositoryDirectory, ["symbolic-ref", "--short", "HEAD"], null, ct)
            .ConfigureAwait(false);

        if (!head.Ok || head.Out.Length == 0)
        {
            return;
        }

        SecretsMirror.CopyInto(
            SecretsMirror.Root(projectRoot, command.RepositoryName, head.Out.Trim()), worktree);
    }

    private async Task<Result<string>> ResolveExactBaseAsync(string anchor, string wanted, CancellationToken ct)
    {
        var remote = AgentBranch.IsRemote(wanted);

        if (await ExistsAsync(anchor, $"refs/heads/{wanted}", ct).ConfigureAwait(false)
            || (remote && await ExistsAsync(anchor, $"refs/remotes/{wanted}", ct).ConfigureAwait(false)))
        {
            return Result<string>.Ok(wanted);
        }

        var hint = remote
            ? " Fetch it first if it is new on origin."
            : await ExistsAsync(anchor, $"refs/remotes/origin/{wanted}", ct).ConfigureAwait(false)
                ? $" It is on origin only: pass 'origin/{wanted}'."
                : string.Empty;

        return Result<string>.Fail($"base '{wanted}' is not a branch in this repository.{hint}");
    }

    private async Task<Result<string>> ResolveBaseRefAsync(
        string anchor, string requested, bool exact, CancellationToken ct)
    {
        var wanted = requested.Trim();

        if (exact && wanted.Length > 0)
        {
            return await ResolveExactBaseAsync(anchor, wanted, ct).ConfigureAwait(false);
        }

        if (AgentBranch.IsRemote(wanted))
        {
            var tracked = await ExistsAsync(anchor, $"refs/remotes/{wanted}", ct)
                .ConfigureAwait(false);

            return tracked
                ? Result<string>.Ok(wanted)
                : Result<string>.Fail($"'{wanted}' is not a branch in this repository.");
        }

        if (wanted.Length == 0)
        {
            var originHead = await git
                .RunAsync(anchor, ["symbolic-ref", "--short", "refs/remotes/origin/HEAD"], null, ct)
                .ConfigureAwait(false);

            var head = await git
                .RunAsync(anchor, ["symbolic-ref", "--short", "HEAD"], null, ct)
                .ConfigureAwait(false);

            wanted = BaseRef.DefaultBranch(
                originHead.Ok ? originHead.Out : string.Empty,
                head.Ok ? head.Out : string.Empty);
        }

        var hasLocal = await ExistsAsync(anchor, $"refs/heads/{wanted}", ct).ConfigureAwait(false);
        var hasRemote = await ExistsAsync(anchor, $"refs/remotes/origin/{wanted}", ct)
            .ConfigureAwait(false);

        if (!hasLocal && !hasRemote)
        {
            return Result<string>.Fail($"'{wanted}' is not a branch in this repository.");
        }

        var ahead = 0;

        if (hasLocal && hasRemote)
        {
            var counts = await git
                .RunAsync(
                    anchor,
                    ["rev-list", "--left-right", "--count", $"{wanted}...origin/{wanted}"],
                    null,
                    ct)
                .ConfigureAwait(false);

            ahead = BaseRef.Ahead(counts.Ok ? counts.Out : string.Empty);
        }

        return Result<string>.Ok(BaseRef.Choose(wanted, hasLocal, hasRemote, ahead));
    }

    private async Task<Result<BranchStart>> AddWorktreeAsync(
        WorktreePlan plan, string branch, string baseRef, CancellationToken ct)
    {
        var local = $"refs/heads/{branch}";
        var remote = $"origin/{branch}";
        var start = new BranchStart();
        string[] args = ["worktree", "add", "--no-track", "-b", branch, plan.TargetDirectory, baseRef];

        if (await ExistsAsync(plan.Anchor, local, ct).ConfigureAwait(false))
        {
            start = new BranchStart(await TipAsync(plan.Anchor, local, ct).ConfigureAwait(false));
            args = ["worktree", "add", plan.TargetDirectory, branch];
        }
        else if (await ExistsAsync(plan.Anchor, $"refs/remotes/{remote}", ct).ConfigureAwait(false))
        {
            start = new BranchStart(
                await TipAsync(plan.Anchor, $"refs/remotes/{remote}", ct).ConfigureAwait(false), remote);
            args = ["worktree", "add", "--track", "-b", branch, plan.TargetDirectory, remote];
        }

        var added = await git.RunAsync(plan.Anchor, args, null, ct).ConfigureAwait(false);

        return added.Ok
            ? Result<BranchStart>.Ok(start)
            : Result<BranchStart>.Fail($"git worktree add: {added.Message}");
    }

    private async Task<string> TipAsync(string directory, string reference, CancellationToken ct)
    {
        var tip = await git.RunAsync(directory, ["rev-parse", "--short", reference], null, ct)
            .ConfigureAwait(false);

        return tip.Ok && tip.Out.Trim().Length > 0 ? tip.Out.Trim() : "an unknown commit";
    }

    private async Task<bool> ExistsAsync(string anchor, string reference, CancellationToken ct) =>
        (await git.RunAsync(anchor, ["rev-parse", "--verify", "--quiet", reference], null, ct)
            .ConfigureAwait(false)).Ok;

    private static Result<StartedAgent> Fail(string reason) => Result<StartedAgent>.Fail(reason);
}
