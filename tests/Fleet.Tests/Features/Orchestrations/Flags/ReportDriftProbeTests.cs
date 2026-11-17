using Fleet.Features.Orchestrations.Flags;
using Fleet.Platform.Git;
using Fleet.Shared.Evidence;

namespace Fleet.Tests.Features.Orchestrations.Flags;

// AC-17: how many commits an agent's HEAD has after the HEAD its done report recorded, against real git.
public sealed class ReportDriftProbeTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "fleet-tests", Path.GetRandomFileName(), "back\\slash");

    private readonly GitRunner _git = new();

    public ReportDriftProbeTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path.GetDirectoryName(_root)!, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private async Task<string> GitAsync(params string[] args)
    {
        var result = await _git.RunAsync(_root, ["-c", "user.name=t", "-c", "user.email=t@t", .. args]);

        Assert.True(result.Ok, $"git {string.Join(' ', args)}: {result.Message}");

        return result.Out.Trim();
    }

    private async Task<string> RepositoryAtReportAsync()
    {
        await GitAsync("init", "-b", "main");
        await GitAsync("commit", "--allow-empty", "-m", "base");
        await GitAsync("commit", "--allow-empty", "-m", "reported");

        return await GitAsync("rev-parse", "HEAD");
    }

    private Task<ReportDrift?> MeasureAsync(string reported) =>
        new ReportDriftProbe(_git).MeasureAsync(_root, reported);

    [Fact]
    public async Task Commits_after_the_reported_head_are_counted()
    {
        var reported = await RepositoryAtReportAsync();
        for (var i = 0; i < 3; i++)
        {
            await GitAsync("commit", "--allow-empty", "-m", $"after {i}");
        }

        var drift = await MeasureAsync(reported);

        Assert.Equal(new ReportDrift(3, false), drift);
        Assert.Equal("3 commits after report", drift!.Label);
    }

    [Fact]
    public async Task A_head_still_at_the_report_has_no_drift()
    {
        var reported = await RepositoryAtReportAsync();

        Assert.Null(await MeasureAsync(reported));
    }

    [Fact]
    public async Task An_amended_report_commit_says_history_was_rewritten_instead_of_a_count()
    {
        var reported = await RepositoryAtReportAsync();
        await GitAsync("commit", "--amend", "--allow-empty", "-m", "reworded");
        await GitAsync("commit", "--allow-empty", "-m", "after");

        var drift = await MeasureAsync(reported);

        Assert.Equal(ReportDrift.HistoryRewritten, drift);
        Assert.Equal("history rewritten since report", drift!.Label);
    }

    [Fact]
    public async Task A_reset_behind_the_report_says_history_was_rewritten()
    {
        var reported = await RepositoryAtReportAsync();
        await GitAsync("reset", "--hard", "HEAD~1");

        Assert.Equal(ReportDrift.HistoryRewritten, await MeasureAsync(reported));
    }

    [Fact]
    public async Task A_reported_head_git_no_longer_knows_says_history_was_rewritten()
    {
        await RepositoryAtReportAsync();

        Assert.Equal(ReportDrift.HistoryRewritten, await MeasureAsync(new string('a', 40)));
    }

    [Fact]
    public async Task No_reported_head_or_no_worktree_measures_nothing()
    {
        await RepositoryAtReportAsync();

        Assert.Null(await MeasureAsync(string.Empty));
        Assert.Null(await new ReportDriftProbe(_git).MeasureAsync(Path.Combine(_root, "gone"), new string('a', 40)));
    }

    [Fact]
    public async Task A_folder_that_is_not_a_repository_measures_nothing()
    {
        Assert.Null(await MeasureAsync(new string('a', 40)));
    }
}
