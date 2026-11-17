using Fleet.Ports.Forge;
using Fleet.Ports.Forge.Enums;
using Fleet.Shared.Results;
using Fleet.Shared.Settings.Enums;
using Fleet.Shared.Settings.Models;

namespace Fleet.Features.Repositories.OpenPr;

public static class ForgeDecision
{
    public static async Task<Result<ForgeKind>> DecideAsync(
        string repository,
        RepositorySettings settings,
        string pushUrl,
        IForgeProbe probe,
        CancellationToken ct = default)
    {
        if (settings.Forge != ForgeKind.Auto)
        {
            return Result<ForgeKind>.Ok(settings.Forge);
        }

        var address = RemoteAddress.Parse(pushUrl);

        if (address is { IsGitHub: true })
        {
            return Result<ForgeKind>.Ok(ForgeKind.GitHub);
        }

        if (address is null)
        {
            return Result<ForgeKind>.Fail(Unknown(repository, $"the push URL '{RemoteAddress.Redacted(pushUrl)}' names no host"));
        }

        return await probe.ProbeAsync(address.ProbeTarget, ct).ConfigureAwait(false) switch
        {
            ForgeProbeResult.ForgejoOrGitea => Result<ForgeKind>.Ok(ForgeKind.Forgejo),
            ForgeProbeResult.Unreachable => Result<ForgeKind>.Fail(
                Unknown(repository, $"https://{address.ProbeTarget} did not answer as a forge just now")),
            _ => Result<ForgeKind>.Fail(
                Unknown(repository, $"{address.ProbeTarget} is not github.com and did not answer as Forgejo or Gitea")),
        };
    }

    public static string Unknown(string repository, string why) =>
        $"fleet cannot tell which forge {repository} is on: {why}. Set \"{RepositorySettings.ForgeKey}\" to "
        + $"{RepositorySettings.ForgeWords} under \"{RepositorySettings.Section}\" > \"{repository}\" in the "
        + $"project's settings file ({RepositorySettings.ForgeSetting(repository)}). Nothing was pushed.";
}
