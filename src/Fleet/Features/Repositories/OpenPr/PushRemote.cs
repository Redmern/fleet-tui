using Fleet.Ports.Git;
using Fleet.Shared.Results;
using Fleet.Shared.Settings.Models;

namespace Fleet.Features.Repositories.OpenPr;

public static class PushRemote
{
    public const string Disabled = "DISABLED";

    public static bool Pushable(string pushUrl) =>
        pushUrl.Trim().Length > 0 && !string.Equals(pushUrl.Trim(), Disabled, StringComparison.OrdinalIgnoreCase);

    public static async Task<Result<string>> UrlAsync(
        IGitRunner git, string workDir, string repository, RepositorySettings settings, CancellationToken ct = default)
    {
        var remote = settings.Remote;

        if (GitNames.Refusal("push remote", remote) is { } refusal)
        {
            return Result<string>.Fail(refusal);
        }

        var url = await git
            .RunAsync(workDir, ["remote", "get-url", "--push", "--all", remote], null, ct)
            .ConfigureAwait(false);

        if (!url.Ok)
        {
            return Result<string>.Fail(
                $"{repository} has no remote named '{remote}' ({url.Message}). The push remote is 'origin' unless "
                + $"{RepositorySettings.PushRemoteSetting(repository)} in the project's settings file names "
                + "another. Nothing was pushed.");
        }

        var urls = url.Out.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (urls.Length > 1)
        {
            return Result<string>.Fail(
                $"Remote '{remote}' in {repository} has {urls.Length} push URLs: fleet pushes a pull request to one "
                + "place only. Nothing was pushed.");
        }

        return urls.Length == 1 && Pushable(urls[0])
            ? Result<string>.Ok(urls[0])
            : Result<string>.Fail(
                $"The push URL of remote '{remote}' in {repository} is "
                + $"{(urls.Length == 0 ? "empty" : "disabled")}: fleet never pushes to it. Nothing was pushed.");
    }
}
