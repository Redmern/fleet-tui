using Fleet.Features.Repositories.ListRepositories.Models;
using Fleet.Ports.Git;

namespace Fleet.Features.Repositories.ListRepositories;

public sealed class ListRepositoriesHandler(IGitRunner git)
{
    public async Task<IReadOnlyList<RepositorySummary>> HandleAsync(
        string projectRoot, CancellationToken ct = default)
    {
        if (!Directory.Exists(projectRoot))
        {
            return [];
        }

        var repos = new List<RepositorySummary>();

        foreach (var dir in Directory.EnumerateDirectories(projectRoot))
        {
            var isBare = await git
                .RunAsync(dir, ["rev-parse", "--is-bare-repository"], null, ct)
                .ConfigureAwait(false);

            if (!isBare.Ok || isBare.Out != "true")
            {
                continue;
            }

            var head = await git
                .RunAsync(dir, ["symbolic-ref", "--short", "HEAD"], null, ct)
                .ConfigureAwait(false);

            repos.Add(new RepositorySummary(
                Path.GetFileName(dir),
                dir,
                head.Ok && head.Out.Length > 0 ? head.Out : "main"));
        }

        return repos.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
