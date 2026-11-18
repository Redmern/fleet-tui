using Fleet.Features.Updates.RunUpdate;
using Fleet.Features.Updates.RunUpdate.Models;
using Fleet.Platform.Releases.Fake;
using Fleet.Ports.Releases;
using Fleet.Ports.Releases.Models;

namespace Fleet.Tests.Features.Updates;

public class IncludeRefreshTests
{
    private const string Executable = "/opt/fleet\\bin/fleet";

    private readonly List<string> _ran = [];
    private int _ownWrites;

    private static RunUpdateCommand Command() => new("owner/repo", "0.1.0", "fleet-linux-x64", Executable);

    private static FakeReleaseClient Newer()
    {
        var client = new FakeReleaseClient
        {
            Release = new ReleaseInfo("v0.2.0", [new ReleaseAsset("fleet-linux-x64", "https://x/asset")]),
        };
        client.Downloads["https://x/asset"] = "new"u8.ToArray();
        return client;
    }

    private async Task<(UpdateOutcome Outcome, string? Note, RecordingInstaller Installer)> UpdateAsync(
        FakeReleaseClient client, bool newBinaryWrites, string? ownError = null)
    {
        var installer = new RecordingInstaller();
        var result = await new RunUpdateHandler(client, installer).HandleAsync(Command());

        Assert.True(result.Succeeded, result.Error);

        var note = IncludeRefresh.After(
            Command(),
            result.Value,
            exe =>
            {
                _ran.Add(exe);
                Assert.Equal(exe, installer.Target);
                return newBinaryWrites;
            },
            () =>
            {
                _ownWrites++;
                return ownError;
            });

        return (result.Value, note, installer);
    }

    [Fact]
    public async Task After_an_install_the_new_binary_writes_the_include_and_the_old_process_does_not()
    {
        var (_, note, installer) = await UpdateAsync(Newer(), newBinaryWrites: true);

        Assert.Equal(Executable, installer.Target);
        Assert.Equal([Executable], _ran);
        Assert.Equal(0, _ownWrites);
        Assert.Contains("v0.2.0", note);
        Assert.Contains("ORCHESTRATOR.md", note);
    }

    [Fact]
    public async Task When_the_new_binary_cannot_write_it_the_old_process_writes_its_own_text_and_says_so()
    {
        var (_, note, _) = await UpdateAsync(Newer(), newBinaryWrites: false);

        Assert.Equal([Executable], _ran);
        Assert.Equal(1, _ownWrites);
        Assert.Contains("0.1.0", note);
        Assert.Contains("fleet setup", note);
    }

    [Fact]
    public async Task When_neither_can_write_it_the_note_carries_the_reason()
    {
        var (_, note, _) = await UpdateAsync(Newer(), newBinaryWrites: false, ownError: "disk full");

        Assert.Contains("disk full", note);
    }

    [Fact]
    public async Task Without_an_install_this_process_is_current_so_it_writes_the_include_itself()
    {
        var client = new FakeReleaseClient { Release = new ReleaseInfo("v0.1.0", []) };

        var (outcome, note, installer) = await UpdateAsync(client, newBinaryWrites: true);

        Assert.False(outcome.Installed);
        Assert.Null(installer.Target);
        Assert.Empty(_ran);
        Assert.Equal(1, _ownWrites);
        Assert.Null(note);
    }

    [Fact]
    public async Task Without_an_install_a_failed_write_is_reported()
    {
        var client = new FakeReleaseClient { Release = new ReleaseInfo("v0.1.0", []) };

        var (_, note, _) = await UpdateAsync(client, newBinaryWrites: true, ownError: "read-only");

        Assert.Contains("read-only", note);
    }

    [Fact]
    public async Task A_downgrade_never_runs_the_older_binary_and_leaves_the_include_alone()
    {
        var client = new FakeReleaseClient();
        client.Versions["0.0.9"] = new ReleaseInfo("v0.0.9", [new ReleaseAsset("fleet-linux-x64", "https://x/asset")]);
        client.Downloads["https://x/asset"] = "old"u8.ToArray();
        var installer = new RecordingInstaller();
        var command = Command() with { RequestedVersion = "0.0.9" };

        var result = await new RunUpdateHandler(client, installer).HandleAsync(command);
        var note = IncludeRefresh.After(command, result.Value, exe =>
        {
            _ran.Add(exe);
            return true;
        }, () =>
        {
            _ownWrites++;
            return null;
        });

        Assert.True(result.Value.Installed);
        Assert.Empty(_ran);
        Assert.Equal(0, _ownWrites);
        Assert.Contains("v0.0.9 is older", note);
    }

    private sealed class RecordingInstaller : IBinaryInstaller
    {
        public string? Target { get; private set; }

        public void Replace(string targetPath, byte[] content) => Target = targetPath;
    }
}
