using Fleet.Features.Agents.NewAgent.Models;
using Fleet.Ports.Git;

namespace Fleet.Features.Agents.NewAgent;

public sealed class ListBranchesHandler(IGitRunner git)
{
    public async Task<IReadOnlyList<BranchChoice>> HandleAsync(
        string repositoryDirectory, CancellationToken ct = default)
    {
        var result = await git
            .RunAsync(
                repositoryDirectory,
                ["for-each-ref", "--format=%(refname:short)", "refs/heads", "refs/remotes"],
                null,
                ct)
            .ConfigureAwait(false);

        return result.Ok ? Parse(result.Out) : [];
    }

    public static IReadOnlyList<BranchChoice> Parse(string output)
    {
        var locals = new List<BranchChoice>();
        var remotes = new List<BranchChoice>();

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var name = line.Trim();

            if (name.Length == 0 || name.EndsWith("/HEAD", StringComparison.Ordinal))
            {
                continue;
            }

            var remote = name.StartsWith("origin/", StringComparison.Ordinal);

            if (remote)
            {
                remotes.Add(new BranchChoice(name, true));
            }
            else
            {
                locals.Add(new BranchChoice(name, false));
            }
        }

        var known = locals.Select(l => l.Reference).ToHashSet(StringComparer.Ordinal);

        return
        [
            .. locals.OrderBy(l => l.Reference, StringComparer.OrdinalIgnoreCase),
            .. remotes
                .Where(r => !known.Contains(r.ShortName))
                .OrderBy(r => r.Reference, StringComparer.OrdinalIgnoreCase),
        ];
    }
}
