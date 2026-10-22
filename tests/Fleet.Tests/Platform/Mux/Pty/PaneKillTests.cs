using Fleet.Platform.Mux.Embedded.Pty;

namespace Fleet.Tests.Platform.Mux.Pty;

public class PaneKillTests
{
    private const int AccessDenied = 5;

    private readonly List<string> _log = [];

    [Fact]
    public void A_pane_with_a_job_kills_the_whole_tree_and_leaves_the_root_alone()
    {
        var rootKilled = false;

        var result = PaneKill.Run(42, job: () => 0, process: () => { rootKilled = true; return 0; }, _log.Add);

        Assert.True(result.Ok);
        Assert.True(result.Tree);
        Assert.False(rootKilled);
        Assert.Empty(_log);
    }

    [Fact]
    public void A_failed_job_kill_is_logged_with_its_win32_error_and_falls_back_to_the_root()
    {
        var rootKilled = false;

        var result = PaneKill.Run(42, job: () => AccessDenied, process: () => { rootKilled = true; return 0; }, _log.Add);

        Assert.True(rootKilled);
        Assert.False(result.Ok);
        Assert.False(result.Tree);
        Assert.Equal(AccessDenied, result.JobError);
        Assert.Null(result.ProcessError);
        var line = Assert.Single(_log);
        Assert.Contains("42", line);
        Assert.Contains("TerminateJobObject", line);
        Assert.Contains("error 5", line);
    }

    [Fact]
    public void A_failed_root_kill_is_logged_and_reported()
    {
        var result = PaneKill.Run(42, job: null, process: () => AccessDenied, _log.Add);

        Assert.False(result.Ok);
        Assert.Null(result.JobError);
        Assert.Equal(AccessDenied, result.ProcessError);
        Assert.Contains("TerminateProcess", Assert.Single(_log));
    }

    [Fact]
    public void Both_failures_are_reported_together()
    {
        var result = PaneKill.Run(42, job: () => AccessDenied, process: () => 6, _log.Add);

        Assert.Equal(AccessDenied, result.JobError);
        Assert.Equal(6, result.ProcessError);
        Assert.Equal(2, _log.Count);
    }

    [Fact]
    public void A_clean_kill_has_no_problem_to_report()
    {
        Assert.Null(PaneKill.Run(42, job: () => 0, process: null, _log.Add).Problem);
    }

    [Fact]
    public void A_failed_kill_describes_every_failed_call_for_the_removal_result()
    {
        var result = PaneKill.Run(42, job: () => AccessDenied, process: () => 6, _log.Add);

        Assert.Contains("42", result.Problem);
        Assert.Contains("TerminateJobObject failed with Win32 error 5", result.Problem);
        Assert.Contains("TerminateProcess failed with Win32 error 6", result.Problem);
    }

    [Fact]
    public void A_pane_without_a_job_whose_root_already_exited_has_nothing_to_kill()
    {
        var result = PaneKill.Run(42, job: null, process: null, _log.Add);

        Assert.True(result.Ok);
        Assert.False(result.Tree);
        Assert.Empty(_log);
    }
}
