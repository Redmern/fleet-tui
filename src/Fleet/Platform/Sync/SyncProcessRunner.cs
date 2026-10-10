using System.Diagnostics;

namespace Fleet.Platform.Sync;

public sealed class SyncProcessRunner : ISyncProcessRunner
{
    public Process Start(ProcessStartInfo start) =>
        Process.Start(start) ?? throw new IOException($"could not start {start.FileName}");
}
