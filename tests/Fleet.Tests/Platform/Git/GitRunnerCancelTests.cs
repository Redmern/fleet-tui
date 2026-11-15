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

        var started = Path.Combine(_root, "started");
        var marker = Path.Combine(_root, "finished");
        using var cancel = new CancellationTokenSource();

        var run = new GitRunner("sh").RunAsync(
            string.Empty, ["-c", $"touch '{started}' && sleep 0.8 && touch '{marker}'"], null, cancel.Token);
        while (!File.Exists(started) && !run.IsCompleted)
        {
            await Task.Delay(10);
        }

        var sinceStart = System.Diagnostics.Stopwatch.StartNew();
        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);

        // Long enough past the start that a child left running would have touched the marker.
        var rest = TimeSpan.FromMilliseconds(1100) - sinceStart.Elapsed;
        if (rest > TimeSpan.Zero)
        {
            await Task.Delay(rest);
        }

        Assert.False(File.Exists(marker));
    }
}
