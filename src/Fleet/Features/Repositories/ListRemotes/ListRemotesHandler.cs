using Fleet.Ports.Git;

namespace Fleet.Features.Repositories.ListRemotes;

public sealed class ListRemotesHandler(IGitRunner git)
{
    public async Task<IReadOnlyList<string>> HandleAsync(
        IReadOnlyList<string> directories, CancellationToken ct = default)
    {
        var urls = new List<string>();

        foreach (var directory in directories)
        {
            var found = await git
                .RunAsync(directory, ["config", "--get", "remote.origin.url"], null, ct)
                .ConfigureAwait(false);

            var url = found.Ok ? found.Out.Trim() : string.Empty;

            if (url.Length > 0 && !urls.Contains(url, StringComparer.OrdinalIgnoreCase))
            {
                urls.Add(url);
            }
        }

        return urls;
    }
}
