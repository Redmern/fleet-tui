using System.ComponentModel;

namespace Fleet.Platform.Mux.Embedded.Pty;

public sealed record PaneKillResult(int Pid, bool Tree, int? JobError, int? ProcessError)
{
    public bool Ok => JobError is null && ProcessError is null;
}

public static class PaneKill
{
    public static PaneKillResult Run(int pid, Func<int>? job, Func<int>? process, Action<string> log)
    {
        int? jobError = null;

        if (job is not null)
        {
            var error = job();
            if (error == 0)
            {
                return new PaneKillResult(pid, true, null, null);
            }

            jobError = error;
            log(Failure(pid, "TerminateJobObject", error) + "; killing only the root process");
        }

        int? processError = null;

        if (process is not null)
        {
            var error = process();
            if (error != 0)
            {
                processError = error;
                log(Failure(pid, "TerminateProcess", error));
            }
        }

        return new PaneKillResult(pid, false, jobError, processError);
    }

    public static string Failure(int pid, string call, int error) =>
        $"pane {pid}: {call} failed with Win32 error {error} ({new Win32Exception(error).Message})";
}
