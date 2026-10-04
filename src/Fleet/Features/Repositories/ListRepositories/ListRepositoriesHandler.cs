using Fleet.Features.Repositories.ListRepositories.Enums;
using Fleet.Features.Repositories.ListRepositories.Models;
using Fleet.Ports.Git;

namespace Fleet.Features.Repositories.ListRepositories;

public sealed class ListRepositoriesHandler(IGitRunner git)
{
    public async Task<IReadOnlyList<RepositorySummary>> HandleAsync(
        string projectRoot, CancellationToken ct = default)
    {
        var probes = RepositoryFolders.Probe(projectRoot);

        var asked = await Task.WhenAll(probes
                .Where(p => p.Kind == FolderKind.Unclear)
                .Select(p => AskGitAsync(p.Directory, ct)))
            .ConfigureAwait(false);

        return probes
            .Where(p => p.Kind == FolderKind.Bare)
            .Select(p => new RepositorySummary(Path.GetFileName(p.Directory), p.Directory, p.DefaultBranch))
            .Concat(asked.OfType<RepositorySummary>())
            .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<RepositorySummary?> AskGitAsync(string dir, CancellationToken ct)
    {
        var isBare = await git
            .RunAsync(dir, ["rev-parse", "--is-bare-repository"], null, ct)
            .ConfigureAwait(false);

        if (!isBare.Ok || isBare.Out != "true")
        {
            return null;
        }

        var head = await git
            .RunAsync(dir, ["symbolic-ref", "--short", "HEAD"], null, ct)
            .ConfigureAwait(false);

        return new RepositorySummary(
            Path.GetFileName(dir),
            dir,
            head.Ok && head.Out.Length > 0 ? head.Out : RepositoryFolders.FallbackBranch);
    }
}
