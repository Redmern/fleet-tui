using Fleet.Features.Agents.NewAgent.Models;
using Fleet.Shared;

namespace Fleet.Features.Agents.NewAgent;

public static class WorktreePlanner
{
    public static WorktreePlan For(
        string repositoryBase, string branch, bool isBare, Func<string, bool> isWorktree)
    {
        if (!isBare)
        {
            return new WorktreePlan(repositoryBase, false, repositoryBase);
        }

        var target = Path.Combine(repositoryBase, BranchSlug.Of(branch));

        return new WorktreePlan(target, !isWorktree(target), repositoryBase);
    }

    public static bool LooksLikeWorktree(string directory) =>
        Directory.Exists(directory)
        && (Directory.Exists(Path.Combine(directory, ".git"))
            || File.Exists(Path.Combine(directory, ".git")));
}
