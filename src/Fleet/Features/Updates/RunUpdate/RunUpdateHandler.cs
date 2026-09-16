using System.Security.Cryptography;
using System.Text;
using Fleet.Features.Updates.RunUpdate.Models;
using Fleet.Ports.Releases;
using Fleet.Shared.Releases;
using Fleet.Shared.Results;

namespace Fleet.Features.Updates.RunUpdate;

public sealed class RunUpdateHandler(IReleaseClient releases, IBinaryInstaller installer)
{
    public async Task<Result<string>> HandleAsync(
        RunUpdateCommand command, CancellationToken ct = default)
    {
        if (command.PlatformAsset is null)
        {
            return Result<string>.Fail(
                "no published binary for this platform. Build from source with "
                + "./install.sh (or install.ps1 on Windows).");
        }

        var latest = await releases.LatestAsync(command.Repo, ct).ConfigureAwait(false);

        if (latest is null)
        {
            return Result<string>.Fail(
                $"no release found at github.com/{command.Repo}/releases (check FLEET_REPO, "
                + "and that a release has been published).");
        }

        if (!VersionCompare.IsNewer(latest.Tag, command.CurrentVersion))
        {
            return Result<string>.Ok($"already on the latest version ({command.CurrentVersion}).");
        }

        var binary = latest.Assets.FirstOrDefault(a => a.Name == command.PlatformAsset);

        if (binary is null)
        {
            return Result<string>.Fail(
                $"release {latest.Tag} has no asset named {command.PlatformAsset}.");
        }

        var bytes = await releases.DownloadAsync(binary.Url, ct).ConfigureAwait(false);

        if (bytes is null)
        {
            return Result<string>.Fail($"could not download {command.PlatformAsset}.");
        }

        var checksum = latest.Assets.FirstOrDefault(a => a.Name == $"{command.PlatformAsset}.sha256");

        if (checksum is not null)
        {
            var verified = await VerifyAsync(bytes, checksum.Url, ct).ConfigureAwait(false);

            if (!verified)
            {
                return Result<string>.Fail(
                    "downloaded binary failed checksum verification. Aborting.");
            }
        }

        try
        {
            installer.Replace(command.ExecutablePath, bytes);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Result<string>.Fail($"could not replace {command.ExecutablePath}: {e.Message}");
        }

        return Result<string>.Ok(
            $"updated to {latest.Tag}. Already-running fleets keep the old code until reopened.");
    }

    private async Task<bool> VerifyAsync(byte[] content, string checksumUrl, CancellationToken ct)
    {
        var sidecar = await releases.DownloadAsync(checksumUrl, ct).ConfigureAwait(false);

        if (sidecar is null)
        {
            return false;
        }

        var wanted = Encoding.ASCII.GetString(sidecar).Split(
            ' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();

        var actual = Convert.ToHexStringLower(SHA256.HashData(content));

        return wanted is not null && string.Equals(wanted, actual, StringComparison.OrdinalIgnoreCase);
    }
}
