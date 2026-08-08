using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace PtySpike;

[StructLayout(LayoutKind.Sequential)]
internal struct Coord
{
    public short X;
    public short Y;
}

[StructLayout(LayoutKind.Sequential)]
internal struct StartupInfoEx
{
    public int cb;
    public IntPtr lpReserved;
    public IntPtr lpDesktop;
    public IntPtr lpTitle;
    public int dwX;
    public int dwY;
    public int dwXSize;
    public int dwYSize;
    public int dwXCountChars;
    public int dwYCountChars;
    public int dwFillAttribute;
    public int dwFlags;
    public short wShowWindow;
    public short cbReserved2;
    public IntPtr lpReserved2;
    public IntPtr hStdInput;
    public IntPtr hStdOutput;
    public IntPtr hStdError;
    public IntPtr lpAttributeList;
}

[StructLayout(LayoutKind.Sequential)]
internal struct ProcessInformation
{
    public IntPtr hProcess;
    public IntPtr hThread;
    public int dwProcessId;
    public int dwThreadId;
}

[SupportedOSPlatform("windows")]
internal static partial class ConPtyNative
{
    internal const int ExtendedStartupInfoPresent = 0x00080000;
    internal const int ProcThreadAttributePseudoConsole = 0x00020016;
    internal const int StillActive = 259;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CreatePipe(
        out SafeFileHandle hReadPipe,
        out SafeFileHandle hWritePipe,
        IntPtr lpPipeAttributes,
        int nSize);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial int CreatePseudoConsole(
        Coord size,
        SafeFileHandle hInput,
        SafeFileHandle hOutput,
        int dwFlags,
        out IntPtr phPC);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial int ResizePseudoConsole(IntPtr hPC, Coord size);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial void ClosePseudoConsole(IntPtr hPC);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool InitializeProcThreadAttributeList(
        IntPtr lpAttributeList,
        int dwAttributeCount,
        int dwFlags,
        ref nuint lpSize);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool UpdateProcThreadAttribute(
        IntPtr lpAttributeList,
        uint dwFlags,
        nuint attribute,
        IntPtr lpValue,
        nuint cbSize,
        IntPtr lpPreviousValue,
        IntPtr lpReturnSize);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial void DeleteProcThreadAttributeList(IntPtr lpAttributeList);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateProcessW", SetLastError = true,
        StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CreateProcess(
        string? lpApplicationName,
        IntPtr lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandles,
        int dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        ref StartupInfoEx lpStartupInfo,
        out ProcessInformation lpProcessInformation);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CloseHandle(IntPtr hObject);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool TerminateProcess(IntPtr hProcess, uint uExitCode);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetExitCodeProcess(IntPtr hProcess, out int lpExitCode);
}
