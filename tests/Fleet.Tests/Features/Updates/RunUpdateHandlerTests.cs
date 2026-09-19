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

    private RunUpdateCommand Command(
        string? platformAsset = "fleet-win-x64.exe",
        string currentVersion = "0.1.0",
        string? requestedVersion = null) =>
        new("owner/repo", currentVersion, platformAsset, _executablePath, requestedVersion);

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

    [Fact]
    public async Task A_requested_version_is_fetched_by_tag_instead_of_latest()
    {
        var newBytes = "pinned"u8.ToArray();
        var client = new FakeReleaseClient
        {
            Release = new ReleaseInfo("v9.9.9", [new ReleaseAsset("fleet-win-x64.exe", "https://x/should-not-use")]),
        };
        client.Versions["0.2.0"] =
            new ReleaseInfo("v0.2.0", [new ReleaseAsset("fleet-win-x64.exe", "https://x/asset")]);
        client.Downloads["https://x/asset"] = newBytes;

        var result = await new RunUpdateHandler(client, new SelfInstall())
            .HandleAsync(Command(currentVersion: "0.1.0", requestedVersion: "0.2.0"));

        Assert.True(result.Succeeded);
        Assert.Contains("v0.2.0", result.Value);
        Assert.Equal(newBytes, File.ReadAllBytes(_executablePath));
    }

    [Fact]
    public async Task A_requested_version_can_be_older_than_the_current_one()
    {
        var oldBytes = "downgraded"u8.ToArray();
        var client = new FakeReleaseClient();
        client.Versions["0.3.0"] =
            new ReleaseInfo("v0.3.0", [new ReleaseAsset("fleet-win-x64.exe", "https://x/asset")]);
        client.Downloads["https://x/asset"] = oldBytes;

        var result = await new RunUpdateHandler(client, new SelfInstall())
            .HandleAsync(Command(currentVersion: "0.5.1", requestedVersion: "0.3.0"));

        Assert.True(result.Succeeded);
        Assert.Contains("v0.3.0", result.Value);
        Assert.Equal(oldBytes, File.ReadAllBytes(_executablePath));
    }

    [Fact]
    public async Task Requesting_the_version_already_installed_does_not_touch_the_binary()
    {
        var client = new FakeReleaseClient();
        client.Versions["0.1.0"] =
            new ReleaseInfo("v0.1.0", [new ReleaseAsset("fleet-win-x64.exe", "https://x/asset")]);

        var result = await new RunUpdateHandler(client, new SelfInstall())
            .HandleAsync(Command(currentVersion: "0.1.0", requestedVersion: "0.1.0"));

        Assert.True(result.Succeeded);
        Assert.Contains("already on", result.Value);
        Assert.Equal("old"u8.ToArray(), File.ReadAllBytes(_executablePath));
    }

    [Fact]
    public async Task Fails_with_a_clear_message_when_the_requested_version_does_not_exist()
    {
        var result = await new RunUpdateHandler(new FakeReleaseClient(), new SelfInstall())
            .HandleAsync(Command(requestedVersion: "9.9.9"));

        Assert.False(result.Succeeded);
        Assert.Contains("9.9.9", result.Error);
    }
}
