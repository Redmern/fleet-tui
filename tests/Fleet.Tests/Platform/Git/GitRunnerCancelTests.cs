using Fleet.Platform.Git;

namespace Fleet.Tests.Platform.Git;

public sealed class GitRunnerCancelTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "fleet-tests", Path.GetRandomFileName());

    public GitRunnerCancelTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    // A cancelled fetch must not leave git running in the background: the
    // child would otherwise finish its work (here: touch the marker) later.
    [Fact]
    public async Task A_cancelled_run_kills_the_process_it_started()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var marker = Path.Combine(_root, "finished");
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new GitRunner("sh").RunAsync(string.Empty, ["-c", $"sleep 1 && touch '{marker}'"], null, cancel.Token));

        await Task.Delay(TimeSpan.FromMilliseconds(1600));

        Assert.False(File.Exists(marker));
    }
}
