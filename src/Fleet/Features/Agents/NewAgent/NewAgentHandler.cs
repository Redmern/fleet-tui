using Fleet.Features.Agents.NewAgent.Models;
using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;
using Fleet.Ports.Git;
using Fleet.Ports.Mux;
using Fleet.Ports.Mux.Models;
using Fleet.Shared;
using Fleet.Shared.Constants;
using Fleet.Shared.Results;

namespace Fleet.Features.Agents.NewAgent;

public sealed class NewAgentHandler(IGitRunner git, IMuxDriver mux, IAgentStore store)
{
    public async Task<Result<AgentRecord>> HandleAsync(
        NewAgentCommand command, CancellationToken ct = default)
    {
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

        if (plan.MustCreate)
        {
            var resolved = await ResolveBaseRefAsync(plan.Anchor, planned.Value!.Base, ct)
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

            await SeedSecretsAsync(command, plan.TargetDirectory, ct).ConfigureAwait(false);
        }

        var agent = new AgentRecord(
            plan.TargetDirectory,
            command.RepositoryName,
            branch,
            command.Harness,
            baseRef,
            plan.TargetDirectory != command.RepositoryDirectory,
            Hidden: false,
            Open: true,
            Owner: command.Owner);

        store.Save(command.ProjectName, agent);

        var pane = await mux.SpawnAsync(
            new SpawnOptions
            {
                Cwd = plan.TargetDirectory,
                SessionName = command.ProjectName,
                Args = AgentHarness.CommandFor(
                    command.Harness, withClaude: command.Owner.Length > 0),
                Env = AgentHarness.SpawnEnv(command.Harness),
            },
            ct).ConfigureAwait(false);

        if (pane.IsNone)
        {
            return Fail($"the {mux.Name} multiplexer did not respond. Run 'fleet doctor'.");
        }

        await mux.SetTitleAsync(pane, AgentTitle.For(agent.Repository, agent.Branch), ct)
            .ConfigureAwait(false);

        return Result<AgentRecord>.Ok(agent);
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

    private async Task<Result<string>> ResolveBaseRefAsync(
        string anchor, string requested, CancellationToken ct)
    {
        var wanted = requested.Trim();

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

    private async Task<Result> AddWorktreeAsync(
        WorktreePlan plan, string branch, string baseRef, CancellationToken ct)
    {
        var exists = await ExistsAsync(plan.Anchor, $"refs/heads/{branch}", ct)
            .ConfigureAwait(false);

        string[] args = exists
            ? ["worktree", "add", plan.TargetDirectory, branch]
            : ["worktree", "add", "--no-track", "-b", branch, plan.TargetDirectory, baseRef];

        var added = await git.RunAsync(plan.Anchor, args, null, ct).ConfigureAwait(false);

        return added.Ok ? Result.Ok() : Result.Fail($"git worktree add: {added.Message}");
    }

    private async Task<bool> ExistsAsync(string anchor, string reference, CancellationToken ct) =>
        (await git.RunAsync(anchor, ["rev-parse", "--verify", "--quiet", reference], null, ct)
            .ConfigureAwait(false)).Ok;

    private static Result<AgentRecord> Fail(string reason) => Result<AgentRecord>.Fail(reason);
}
