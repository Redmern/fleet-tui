using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;
using Fleet.Shared.Orchestrations;
using Fleet.Shared.Results;

namespace Fleet.Features.Orchestrations.RenameOrchestration;

public sealed class RenameOrchestrationHandler(IAgentStore store)
{
    public Result<AgentRecord> Handle(
        string project, string projectRoot, AgentRecord sub, string newSlug)
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

        var newFolder = OrchestrationPaths.For(projectRoot, slug);

        if (Directory.Exists(newFolder))
        {
            return Result<AgentRecord>.Fail($"An orchestration named '{slug}' already exists.");
        }

        try
        {
            Directory.Move(sub.Worktree, newFolder);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Result<AgentRecord>.Fail($"Could not move the orchestration folder: {e.Message}");
        }

        var updated = sub with { Branch = slug, Worktree = newFolder };

        store.Save(project, updated);
        store.Remove(project, sub.Worktree);

        foreach (var child in store.List(project)
            .Where(a => string.Equals(a.Owner, sub.Branch, StringComparison.OrdinalIgnoreCase)))
        {
            store.Save(project, child with { Owner = slug });
        }

        return Result<AgentRecord>.Ok(updated);
    }
}
