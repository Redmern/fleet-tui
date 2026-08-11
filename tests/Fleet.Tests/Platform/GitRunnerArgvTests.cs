using Fleet.Platform.Git;

namespace Fleet.Tests.Platform;

public class GitRunnerArgvTests
{
    [Fact]
    public void The_working_directory_is_passed_to_git_rather_than_to_the_process()
    {
        Assert.Equal(
            ["-C", "C:/repos/techweb/backend", "status", "--porcelain"],
            GitRunner.Argv("C:/repos/techweb/backend", ["status", "--porcelain"]));
    }

    [Fact]
    public void No_directory_means_git_runs_where_fleet_already_is()
    {
        Assert.Equal(["--version"], GitRunner.Argv(string.Empty, ["--version"]));
        Assert.Equal(["--version"], GitRunner.Argv("   ", ["--version"]));
    }
}
