using Fleet.Features.Repositories.Secrets.Models;
using Fleet.Shared;

namespace Fleet.Features.Repositories.Secrets;

public sealed class SecretsHandler
{
    public SecretsPlan Plan(
        string projectRoot, string repository, string container, string defaultBranch)
    {
        var root = SecretsMirror.Root(projectRoot, repository, defaultBranch);

        return new SecretsPlan(repository, root, SecretsMirror.Files(root), Worktrees(container));
    }

    public int Distribute(SecretsPlan plan)
    {
        if (plan.Files.Count == 0)
        {
            return 0;
        }

        return plan.Worktrees.Count(w => SecretsMirror.CopyInto(plan.Root, w) > 0);
    }

    public static IReadOnlyList<string> Worktrees(string container)
    {
        if (!Directory.Exists(container))
        {
            return [];
        }

        return
        [
            .. Directory.EnumerateDirectories(container)
                .Where(d => File.Exists(Path.Combine(d, ".git"))
                    || Directory.Exists(Path.Combine(d, ".git")))
                .OrderBy(d => d, StringComparer.OrdinalIgnoreCase),
        ];
    }
}
