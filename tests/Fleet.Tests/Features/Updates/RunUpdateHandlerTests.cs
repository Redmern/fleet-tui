using System.Security.Cryptography;
using System.Text;
using Fleet.Features.Updates.RunUpdate;
using Fleet.Features.Updates.RunUpdate.Models;
using Fleet.Platform.Releases;
using Fleet.Platform.Releases.Fake;
using Fleet.Ports.Releases.Models;

namespace Fleet.Tests.Features.Updates;

public class RunUpdateHandlerTests : IDisposable
{
    private readonly string _executablePath =
        Path.Combine(Path.GetTempPath(), $"fleet-update-test-{Guid.NewGuid():N}.exe");

    public RunUpdateHandlerTests() => File.WriteAllBytes(_executablePath, "old"u8.ToArray());

    public void Dispose()
    {
        foreach (var file in Directory.GetFiles(
            Path.GetDirectoryName(_executablePath)!, $"{Path.GetFileName(_executablePath)}*"))
        {
            File.Delete(file);
        }
    }

    private RunUpdateCommand Command(string? platformAsset = "fleet-win-x64.exe") =>
        new("owner/repo", "0.1.0", platformAsset, _executablePath);

    [Fact]
    public async Task Refuses_a_platform_with_no_published_asset()
    {
        var result = await new RunUpdateHandler(new FakeReleaseClient(), new SelfInstall())
            .HandleAsync(Command(platformAsset: null));

        Assert.False(result.Succeeded);
        Assert.Contains("no published binary", result.Error);
    }

    [Fact]
    public async Task Fails_when_no_release_is_found()
    {
        var result = await new RunUpdateHandler(new FakeReleaseClient { Release = null }, new SelfInstall())
            .HandleAsync(Command());

        Assert.False(result.Succeeded);
        Assert.Contains("owner/repo", result.Error);
    }

    [Fact]
    public async Task Reports_already_up_to_date_without_touching_the_binary()
    {
        var client = new FakeReleaseClient { Release = new ReleaseInfo("v0.1.0", []) };

        var result = await new RunUpdateHandler(client, new SelfInstall()).HandleAsync(Command());

        Assert.True(result.Succeeded);
        Assert.Contains("already on the latest version", result.Value);
        Assert.Equal("old"u8.ToArray(), File.ReadAllBytes(_executablePath));
    }

    [Fact]
    public async Task Fails_when_the_release_has_no_matching_asset()
    {
        var client = new FakeReleaseClient
        {
            Release = new ReleaseInfo("v0.2.0", [new ReleaseAsset("fleet-linux-x64", "https://x/linux")]),
        };

        var result = await new RunUpdateHandler(client, new SelfInstall()).HandleAsync(Command());

        Assert.False(result.Succeeded);
        Assert.Contains("fleet-win-x64.exe", result.Error);
    }

    [Fact]
    public async Task Downloads_and_replaces_the_binary_when_a_newer_release_exists()
    {
        var newBytes = "new"u8.ToArray();
        var client = new FakeReleaseClient
        {
            Release = new ReleaseInfo(
                "v0.2.0", [new ReleaseAsset("fleet-win-x64.exe", "https://x/asset")]),
        };
        client.Downloads["https://x/asset"] = newBytes;

        var result = await new RunUpdateHandler(client, new SelfInstall()).HandleAsync(Command());

        Assert.True(result.Succeeded);
        Assert.Contains("v0.2.0", result.Value);
        Assert.Equal(newBytes, File.ReadAllBytes(_executablePath));
    }

    [Fact]
    public async Task Verifies_the_checksum_when_a_sidecar_asset_is_published()
    {
        var newBytes = "new"u8.ToArray();
        var hash = Convert.ToHexStringLower(SHA256.HashData(newBytes));
        var client = new FakeReleaseClient
        {
            Release = new ReleaseInfo(
                "v0.2.0",
                [
                    new ReleaseAsset("fleet-win-x64.exe", "https://x/asset"),
                    new ReleaseAsset("fleet-win-x64.exe.sha256", "https://x/asset.sha256"),
                ]),
        };
        client.Downloads["https://x/asset"] = newBytes;
        client.Downloads["https://x/asset.sha256"] = Encoding.ASCII.GetBytes($"{hash}  fleet-win-x64.exe\n");

        var result = await new RunUpdateHandler(client, new SelfInstall()).HandleAsync(Command());

        Assert.True(result.Succeeded);
        Assert.Equal(newBytes, File.ReadAllBytes(_executablePath));
    }

    [Fact]
    public async Task Refuses_to_install_when_the_checksum_does_not_match()
    {
        var newBytes = "new"u8.ToArray();
        var client = new FakeReleaseClient
        {
            Release = new ReleaseInfo(
                "v0.2.0",
                [
                    new ReleaseAsset("fleet-win-x64.exe", "https://x/asset"),
                    new ReleaseAsset("fleet-win-x64.exe.sha256", "https://x/asset.sha256"),
                ]),
        };
        client.Downloads["https://x/asset"] = newBytes;
        client.Downloads["https://x/asset.sha256"] =
            Encoding.ASCII.GetBytes("0000000000000000000000000000000000000000000000000000000000000000\n");

        var result = await new RunUpdateHandler(client, new SelfInstall()).HandleAsync(Command());

        Assert.False(result.Succeeded);
        Assert.Contains("checksum", result.Error);
        Assert.Equal("old"u8.ToArray(), File.ReadAllBytes(_executablePath));
    }
}
