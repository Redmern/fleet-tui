using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;
using Fleet.Ports.Mux;
using Fleet.Shared;
using Fleet.Shared.Constants;
using Fleet.Shared.Results;

namespace Fleet.Features.Orchestrations.RenameOrchestration;

public sealed class RenameOrchestrationHandler(IAgentStore store, IMuxDriver? mux = null)
{
    public async Task<Result<AgentRecord>> HandleAsync(
        string project, AgentRecord sub, string newSlug, CancellationToken ct = default)
    {
        var panes = mux is null
            ? []
            : await mux.ListPanesAsync(ct).ConfigureAwait(false);

        var mine = panes.Where(p => AgentPaneMatch.Owns(p, sub)).ToList();

        var result = Handle(project, sub, newSlug);

        if (!result.Succeeded || mux is null)
        {
            return result;
        }

        var title = AgentTitle.For(result.Value!.Repository, result.Value.Branch);

        foreach (var tab in mine.GroupBy(p => p.TabId))
        {
            await mux.SetTitleAsync(tab.First().Id, title, ct).ConfigureAwait(false);
        }

        return result;
    }

    public Result<AgentRecord> Handle(string project, AgentRecord sub, string newSlug)
    {
        var slug = newSlug.Trim();

        if (slug.Length == 0)
        {
            return Result<AgentRecord>.Fail("A name is required.");
        }

        if (string.Equals(slug, sub.Branch, StringComparison.OrdinalIgnoreCase))
        {
            return Result<AgentRecord>.Fail("That is already its name.");
        }

        var taken = store.List(project).Any(a =>
            AgentHarness.IsOrchestrator(a.Harness)
            && string.Equals(a.Branch, slug, StringComparison.OrdinalIgnoreCase)
            && !PathKey.Same(a.Worktree, sub.Worktree));

        if (taken)
        {
            return Result<AgentRecord>.Fail($"An orchestration named '{slug}' already exists.");
        }

        var updated = sub with { Branch = slug };

        store.Save(project, updated);

        foreach (var child in store.List(project)
            .Where(a => string.Equals(a.Owner, sub.Branch, StringComparison.OrdinalIgnoreCase)))
        {
            store.Save(project, child with { Owner = slug });
        }

        return Result<AgentRecord>.Ok(updated);
    }
}
