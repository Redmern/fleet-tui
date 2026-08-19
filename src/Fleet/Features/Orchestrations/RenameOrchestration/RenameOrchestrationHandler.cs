using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;
using Fleet.Shared;
using Fleet.Shared.Constants;
using Fleet.Shared.Results;

namespace Fleet.Features.Orchestrations.RenameOrchestration;

public sealed class RenameOrchestrationHandler(IAgentStore store)
{
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
