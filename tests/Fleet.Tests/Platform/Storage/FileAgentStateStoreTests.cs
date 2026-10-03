using Fleet.Platform.Storage;
using Fleet.Shared.Status.Enums;
using Fleet.Shared.Status.Models;

namespace Fleet.Tests.Platform.Storage;

public sealed class FileAgentStateStoreTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "fleet-tests", Path.GetRandomFileName());

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static AgentReport Report(AgentState state, string session = "s1", string worktree = "C:/w/a", DateTime? at = null) =>
        new(worktree, session, state, at ?? DateTime.UtcNow, "C:/t/s1.jsonl", "permission");

    [Fact]
    public async Task A_missing_folder_is_an_empty_snapshot_not_an_error()
    {
        var snapshot = await new FileAgentStateStore(_dir).GetSnapshotAsync();

        Assert.Empty(snapshot.Reports);
    }

    [Fact]
    public async Task A_report_reads_back_with_everything_it_carried()
    {
        var store = new FileAgentStateStore(_dir);
        var sent = Report(AgentState.Blocked);

        await store.ReportAsync(sent);

        var read = Assert.Single((await store.GetSnapshotAsync()).Reports);
        Assert.Equal(sent.Worktree, read.Worktree);
        Assert.Equal(sent.Session, read.Session);
        Assert.Equal(AgentState.Blocked, read.State);
        Assert.Equal(sent.Transcript, read.Transcript);
        Assert.Equal(sent.Reason, read.Reason);
        Assert.Equal(sent.At, read.At);
    }

    [Fact]
    public async Task One_file_per_session_and_the_latest_report_wins()
    {
        var store = new FileAgentStateStore(_dir);

        await store.ReportAsync(Report(AgentState.Working, "s1"));
        await store.ReportAsync(Report(AgentState.Idle, "s1"));
        await store.ReportAsync(Report(AgentState.Working, "s2"));

        var reports = (await store.GetSnapshotAsync()).Reports;

        Assert.Equal(2, reports.Count);
        Assert.Equal(AgentState.Idle, reports.Single(r => r.Session == "s1").State);
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }

    [Fact]
    public async Task A_cleared_report_removes_the_session()
    {
        var store = new FileAgentStateStore(_dir);

        await store.ReportAsync(Report(AgentState.Working));
        await store.ReportAsync(Report(AgentState.Unknown));

        Assert.Empty((await store.GetSnapshotAsync()).Reports);
    }

    [Fact]
    public async Task Reports_older_than_a_day_are_dropped_and_their_files_deleted()
    {
        var store = new FileAgentStateStore(_dir);

        await store.ReportAsync(Report(AgentState.Working, at: DateTime.UtcNow - TimeSpan.FromDays(2)));

        Assert.Empty((await store.GetSnapshotAsync()).Reports);
        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Fact]
    public async Task A_corrupt_file_is_skipped()
    {
        var store = new FileAgentStateStore(_dir);

        await store.ReportAsync(Report(AgentState.Working));
        await File.WriteAllTextAsync(Path.Combine(_dir, "broken.json"), "{ not json");

        Assert.Single((await store.GetSnapshotAsync()).Reports);
    }

    [Fact]
    public async Task A_new_session_forgets_the_sessions_before_it_in_the_same_worktree()
    {
        var store = new FileAgentStateStore(_dir);

        await store.ReportAsync(Report(AgentState.Working, "killed"));
        await store.ReportAsync(Report(AgentState.Blocked, "elsewhere", worktree: "C:/w/b"));
        await store.ReportAsync(Report(AgentState.Idle, "fresh") with { StartsSession = true });

        var reports = (await store.GetSnapshotAsync()).Reports;

        Assert.Equal(["elsewhere", "fresh"], reports.Select(r => r.Session).Order());
    }

    [Fact]
    public async Task A_session_end_clears_the_report_while_a_reader_has_the_file_open()
    {
        var store = new FileAgentStateStore(_dir);
        await store.ReportAsync(Report(AgentState.Working));
        var file = Directory.GetFiles(_dir, "*.json").Single();

        using (new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            await store.ReportAsync(Report(AgentState.Unknown));
        }

        Assert.Empty((await store.GetSnapshotAsync()).Reports);
    }

    [Fact]
    public async Task A_report_waits_out_a_file_briefly_held_shut()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var store = new FileAgentStateStore(_dir);
        await store.ReportAsync(Report(AgentState.Working));
        var file = Directory.GetFiles(_dir, "*.json").Single();

        using (HeldShut.For(file, TimeSpan.FromMilliseconds(200)))
        {
            await store.ReportAsync(Report(AgentState.Idle));
        }

        Assert.Equal(AgentState.Idle, Assert.Single((await store.GetSnapshotAsync()).Reports).State);
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }

    [Fact]
    public async Task Leftover_temp_files_are_cleaned_up_once_they_are_old()
    {
        Directory.CreateDirectory(_dir);
        var old = Path.Combine(_dir, "x.json.abc.tmp");
        var recent = Path.Combine(_dir, "y.json.def.tmp");
        await File.WriteAllTextAsync(old, "{}");
        await File.WriteAllTextAsync(recent, "{}");
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow - TimeSpan.FromMinutes(5));

        await new FileAgentStateStore(_dir).GetSnapshotAsync();

        Assert.False(File.Exists(old));
        Assert.True(File.Exists(recent));
    }

    [Fact]
    public void The_file_name_is_stable_for_the_same_worktree_spelled_differently()
    {
        var a = FileAgentStateStore.NameFor(@"C:\w\a\", "s1");
        var b = FileAgentStateStore.NameFor("C:/w/a", "s1");

        if (OperatingSystem.IsWindows())
        {
            Assert.Equal(a, b);
            Assert.Equal(a, FileAgentStateStore.NameFor("c:/W/A", "s1"));
        }

        Assert.NotEqual(b, FileAgentStateStore.NameFor("C:/w/b", "s1"));
    }

    [Fact]
    public void A_session_id_cannot_escape_the_folder()
    {
        var name = FileAgentStateStore.NameFor("C:/w/a", "../../evil");

        Assert.DoesNotContain("..", name);
        Assert.DoesNotContain("/", name);
    }
}
