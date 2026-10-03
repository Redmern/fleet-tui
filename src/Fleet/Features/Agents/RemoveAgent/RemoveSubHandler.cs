using Fleet.Features.Agents.RemoveAgent.Models;
using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;
using Fleet.Shared.Constants;
using Fleet.Shared.Results;

namespace Fleet.Features.Agents.RemoveAgent;

public sealed class RemoveSubHandler(RemoveAgentHandler remover, IAgentStore store)
{
    public static AgentRecord? Find(IReadOnlyList<AgentRecord> agents, string slug) =>
        agents.FirstOrDefault(a => AgentHarness.IsOrchestrator(a.Harness)
                                   && string.Equals(a.Branch, slug, StringComparison.OrdinalIgnoreCase));

    public static string NotFound(string slug) => $"No sub-orchestrator named '{slug}' in this project.";

    public async Task<Result<SubRemoval>> HandleAsync(RemoveSubCommand command, CancellationToken ct = default)
    {
        var agents = store.List(command.Project);
        var sub = Find(agents, command.Slug);

        if (sub is null)
        {
            return Result<SubRemoval>.Fail(NotFound(command.Slug));
        }

        if (string.Equals(command.Caller.Trim(), sub.Branch, StringComparison.OrdinalIgnoreCase))
        {
            return Result<SubRemoval>.Fail(
                $"{sub.Branch} is you; a sub-orchestrator can't remove itself. Report done and let "
                + "the orchestrator that dispatched you remove it.");
        }

        if (OrchestrationStatus.Normalize(sub.Status) == OrchestrationStatus.Working)
        {
            return Result<SubRemoval>.Fail(
                $"{sub.Branch} is still working. Wait until it reports done or failed, "
                + "or remove it from the dashboard's Subs tab.");
        }

        var gone = await remover.HandleAsync(command.Project, sub, command.DeleteFolder, ct)
            .ConfigureAwait(false);

        if (!gone.Succeeded)
        {
            return Result<SubRemoval>.Fail(gone.Error!);
        }

        var removed = new List<string>();
        var kept = new List<string>();
        var released = new List<string>();

        foreach (var child in SubChildren.Of(agents, sub.Branch))
        {
            var label = $"{child.Repository}/{child.Branch}";

            if (!command.RemoveAgents)
            {
                store.Save(command.Project, SubChildren.Released(child));
                released.Add(label);
                continue;
            }

            var refusal = await RemoveOrRefuseAsync(command.Project, child, ct).ConfigureAwait(false);

            if (refusal is null)
            {
                removed.Add(label);
                continue;
            }

            store.Save(command.Project, SubChildren.Released(child));
            kept.Add($"{label} ({refusal})");
        }

        return Result<SubRemoval>.Ok(
            new SubRemoval(sub.Branch, sub.Worktree, command.DeleteFolder, removed, kept, released));
    }

    private async Task<string?> RemoveOrRefuseAsync(string project, AgentRecord child, CancellationToken ct)
    {
        var state = await remover.InspectAsync(child, ct).ConfigureAwait(false);

        if (state.IsDirty)
        {
            return $"{state.Changed.Count} uncommitted change(s)";
        }

        var unpushed = await remover.UnpushedAsync(child, ct).ConfigureAwait(false);

        if (unpushed is null)
        {
            return "could not check for unpushed commits";
        }

        if (unpushed > 0)
        {
            return $"{unpushed} unpushed commit(s)";
        }

        var gone = await remover.HandleAsync(project, child, deleteWorktree: true, ct).ConfigureAwait(false);

        return gone.Succeeded ? null : gone.Error;
    }
}
