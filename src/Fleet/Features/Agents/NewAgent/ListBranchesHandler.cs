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
                ["for-each-ref", "--format=%(refname)", "refs/heads", "refs/remotes"],
                null,
                ct)
            .ConfigureAwait(false);

        return result.Ok ? Parse(result.Out) : [];
    }

    private const string LocalPrefix = "refs/heads/";

    private const string RemotePrefix = "refs/remotes/origin/";

    public static IReadOnlyList<BranchChoice> Parse(string output)
    {
        var locals = new List<BranchChoice>();
        var remotes = new List<BranchChoice>();

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var reference = line.Trim();

            if (reference.StartsWith(LocalPrefix, StringComparison.Ordinal))
            {
                var name = reference[LocalPrefix.Length..];

                if (name.Length > 0)
                {
                    locals.Add(new BranchChoice(name, false));
                }

                continue;
            }

            if (!reference.StartsWith(RemotePrefix, StringComparison.Ordinal))
            {
                continue;
            }

            var branch = reference[RemotePrefix.Length..];

            if (branch.Length > 0 && branch != "HEAD")
            {
                remotes.Add(new BranchChoice("origin/" + branch, true));
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
