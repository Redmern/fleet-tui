using Fleet.Platform.Forge;
using Fleet.Ports.Forge.Models;

namespace Fleet.Tests.Platform.Forge;

public sealed class ProcessRunnerTests
{
    [Fact]
    public async Task A_command_runs_in_the_folder_with_its_arguments_and_gives_its_output_and_exit_code()
    {
        var folder = Path.GetTempPath();

        var shown = await new ProcessRunner().RunAsync("git", ["--version"], folder);

        Assert.True(shown.Ok, shown.Message);
        Assert.StartsWith("git version", shown.Out);

        var failed = await new ProcessRunner().RunAsync("git", ["no-such-command"], folder);

        Assert.False(failed.Ok);
        Assert.Contains("no-such-command", failed.Message);
    }

    [Fact]
    public async Task A_command_that_is_not_installed_is_reported_as_missing_not_thrown()
    {
        var result = await new ProcessRunner().RunAsync(
            "fleet-no-such-program-" + Guid.NewGuid().ToString("N"), [], Path.GetTempPath());

        Assert.True(result.Missing);
        Assert.Equal(ProcessResult.NotFound, result.ExitCode);
    }

    [Fact]
    public async Task A_command_never_reads_the_input_of_the_process_that_started_it()
    {
        // `git hash-object --stdin` reads its input to the end: it returns at once only when that input is closed.
        var hashed = await new ProcessRunner()
            .RunAsync("git", ["hash-object", "--stdin"], Path.GetTempPath())
            .WaitAsync(TimeSpan.FromSeconds(30));

        Assert.True(hashed.Ok, hashed.Message);
        Assert.Equal("e69de29bb2d1d6434b8b29ae775ad8c2e48c5391", hashed.Out);
    }
}
