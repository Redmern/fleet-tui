using Fleet.Features.Agents.RemoveAgent;

namespace Fleet.Tests.Features.Agents;

public class WorktreeLockTests
{
    [Theory]
    [InlineData("error: failed to delete 'C:/repos/x/test2': Permission denied")]
    [InlineData("The process cannot access the file because it is being used by another process.")]
    [InlineData("Access is denied")]
    public void A_locked_worktree_is_recognised_as_worth_retrying(string message)
    {
        Assert.True(WorktreeLock.LooksBusy(message));
    }

    [Theory]
    [InlineData("fatal: 'test2' is not a valid ref")]
    [InlineData("fatal: not a git repository")]
    public void An_unrelated_git_failure_is_not_treated_as_a_lock(string message)
    {
        Assert.False(WorktreeLock.LooksBusy(message));
    }

    [Fact]
    public void Git_having_already_dropped_its_bookkeeping_is_recognised()
    {
        Assert.True(WorktreeLock.AlreadyUnregistered("fatal: 'test2' is not a working tree"));
        Assert.False(WorktreeLock.AlreadyUnregistered("error: failed to delete"));
    }

    [Fact]
    public void The_busy_message_names_the_path_and_says_the_files_were_kept()
    {
        var message = WorktreeLock.Busy(@"C:\repos\techweb\backend\test2");

        Assert.Contains(@"C:\repos\techweb\backend\test2", message);
        Assert.Contains("still in use", message);
        Assert.Contains("kept", message);
    }
}
