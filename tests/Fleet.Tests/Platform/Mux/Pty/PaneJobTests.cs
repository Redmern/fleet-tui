using System.Diagnostics;
using Fleet.Platform.Mux.Embedded.Pty;

namespace Fleet.Tests.Platform.Mux.Pty;

public class PaneJobTests
{
    private static Process Ping() =>
        Process.Start(new ProcessStartInfo("cmd.exe", "/c ping -n 600 127.0.0.1 >nul") { CreateNoWindow = true })!;

    [Fact]
    public void Terminating_the_job_ends_the_process_assigned_to_it()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var job = PaneJob.Create(out var created);
        Assert.Equal(0, created);
        using var process = Ping();

        try
        {
            Assert.Equal(0, job!.Assign(process.Handle));
            Assert.Equal(0, job.Terminate());
            Assert.True(process.WaitForExit(5000));
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(true);
            }
        }
    }

    [Fact]
    public void Closing_the_job_ends_the_process_assigned_to_it()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var job = PaneJob.Create(out _);
        using var process = Ping();

        try
        {
            Assert.Equal(0, job!.Assign(process.Handle));
            job.Dispose();
            Assert.True(process.WaitForExit(5000));
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(true);
            }
        }
    }

    [Fact]
    public void Terminating_a_disposed_job_reports_an_error_instead_of_throwing()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var job = PaneJob.Create(out _)!;
        job.Dispose();

        Assert.NotEqual(0, job.Terminate());
    }
}
