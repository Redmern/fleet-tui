using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Fleet.Platform.Mux.Embedded.Pty;

[SupportedOSPlatform("windows")]
public sealed partial class PaneJob : IDisposable
{
    private const int ExtendedLimitInformation = 9;
    private const uint KillOnJobClose = 0x2000;
    private const int InvalidHandle = 6;

    private nint _job;

    private PaneJob(nint job) => _job = job;

    public static PaneJob? Create(out int error)
    {
        var job = CreateJobObjectW(0, 0);
        if (job == 0)
        {
            error = Marshal.GetLastPInvokeError();
            return null;
        }

        var limits = new ExtendedLimit { Basic = new BasicLimit { LimitFlags = KillOnJobClose } };
        if (!SetInformationJobObject(job, ExtendedLimitInformation, ref limits, (uint)Marshal.SizeOf<ExtendedLimit>()))
        {
            error = Marshal.GetLastPInvokeError();
            CloseHandle(job);
            return null;
        }

        error = 0;
        return new PaneJob(job);
    }

    public int Assign(nint process) =>
        _job == 0 ? InvalidHandle : AssignProcessToJobObject(_job, process) ? 0 : Marshal.GetLastPInvokeError();

    public int Terminate(uint code = 1) =>
        _job == 0 ? InvalidHandle : TerminateJobObject(_job, code) ? 0 : Marshal.GetLastPInvokeError();

    public void Dispose()
    {
        var job = Interlocked.Exchange(ref _job, 0);
        if (job != 0)
        {
            CloseHandle(job);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimit
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimit
    {
        public BasicLimit Basic;
        public IoCounters Io;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint CreateJobObjectW(nint attributes, nint name);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetInformationJobObject(nint job, int infoClass, ref ExtendedLimit info, uint length);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AssignProcessToJobObject(nint job, nint process);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TerminateJobObject(nint job, uint code);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);
}
