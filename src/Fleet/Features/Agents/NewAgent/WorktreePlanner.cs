using Fleet.Features.Agents.NewAgent.Models;
using Fleet.Shared;

namespace Fleet.Features.Agents.NewAgent;

public static class WorktreePlanner
{
    public static WorktreePlan For(
        string repositoryBase, string branch, bool isBare, Func<string, bool> directoryExists)
    {
        if (!isBare)
        {
            return new WorktreePlan(repositoryBase, false, repositoryBase);
        }

        var target = Path.Combine(repositoryBase, BranchSlug.Of(branch));

        return new WorktreePlan(target, !directoryExists(target), repositoryBase);
    }
}
