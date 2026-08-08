using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace PtySpike;

[SupportedOSPlatform("windows")]
public sealed class ConPtyConnection : IDisposable
{
    private readonly IntPtr _pseudoConsole;
    private readonly IntPtr _process;
    private readonly IntPtr _thread;
    private readonly IntPtr _attributeList;
    private bool _disposed;

    internal ConPtyConnection(
        IntPtr pseudoConsole,
        IntPtr process,
        IntPtr thread,
        IntPtr attributeList,
        int pid,
        Stream reader,
        Stream writer)
    {
        _pseudoConsole = pseudoConsole;
        _process = process;
        _thread = thread;
        _attributeList = attributeList;
        Pid = pid;
        ReaderStream = reader;
        WriterStream = writer;
    }

    public int Pid { get; }

    public Stream ReaderStream { get; }

    public Stream WriterStream { get; }

    public bool HasExited
    {
        get
        {
            if (!ConPtyNative.GetExitCodeProcess(_process, out var code))
            {
                return true;
            }

            return code != ConPtyNative.StillActive;
        }
    }

    public int ExitCode => ConPtyNative.GetExitCodeProcess(_process, out var code) ? code : -1;

    public void Resize(int cols, int rows)
    {
        var size = new Coord { X = (short)Math.Max(cols, 1), Y = (short)Math.Max(rows, 1) };
        ConPtyNative.ResizePseudoConsole(_pseudoConsole, size);
    }

    public bool WaitForExit(int milliseconds) =>
        ConPtyNative.WaitForSingleObject(_process, (uint)milliseconds) == 0;

    public void Kill()
    {
        if (!HasExited)
        {
            ConPtyNative.TerminateProcess(_process, 1);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        ReaderStream.Dispose();
        WriterStream.Dispose();

        ConPtyNative.ClosePseudoConsole(_pseudoConsole);

        if (_attributeList != IntPtr.Zero)
        {
            ConPtyNative.DeleteProcThreadAttributeList(_attributeList);
            Marshal.FreeHGlobal(_attributeList);
        }

        if (_thread != IntPtr.Zero)
        {
            ConPtyNative.CloseHandle(_thread);
        }

        if (_process != IntPtr.Zero)
        {
            ConPtyNative.CloseHandle(_process);
        }
    }
}

[SupportedOSPlatform("windows")]
public static class ConPty
{
    public static Action<string>? Trace { get; set; }

    public static ConPtyConnection Spawn(string commandLine, string cwd, int cols, int rows)
    {
        if (!ConPtyNative.CreatePipe(out var inputRead, out var inputWrite, IntPtr.Zero, 0))
        {
            throw Fail("CreatePipe (input)");
        }

        if (!ConPtyNative.CreatePipe(out var outputRead, out var outputWrite, IntPtr.Zero, 0))
        {
            throw Fail("CreatePipe (output)");
        }

        var size = new Coord { X = (short)Math.Max(cols, 1), Y = (short)Math.Max(rows, 1) };

        var hr = ConPtyNative.CreatePseudoConsole(size, inputRead, outputWrite, 0, out var pc);

        if (hr != 0)
        {
            throw new Win32Exception(hr, $"CreatePseudoConsole failed with HRESULT 0x{hr:X8}");
        }

        Trace?.Invoke($"CreatePseudoConsole hr=0x{hr:X8} hpc=0x{pc:X}");

        inputRead.Dispose();
        outputWrite.Dispose();

        var attributeList = BuildAttributeList(pc);

        var cb = Environment.GetEnvironmentVariable("PTYSPIKE_CB") == "startupinfo"
            ? Marshal.SizeOf<StartupInfoEx>() - IntPtr.Size
            : Marshal.SizeOf<StartupInfoEx>();

        var startup = new StartupInfoEx
        {
            cb = cb,
            lpAttributeList = attributeList,
        };

        Trace?.Invoke($"STARTUPINFOEX cb={startup.cb} attrList=0x{attributeList:X}");

        var commandLinePtr = Marshal.StringToHGlobalUni(commandLine);

        try
        {
            var created = ConPtyNative.CreateProcess(
                null,
                commandLinePtr,
                IntPtr.Zero,
                IntPtr.Zero,
                Environment.GetEnvironmentVariable("PTYSPIKE_INHERIT") == "true",
                ConPtyNative.ExtendedStartupInfoPresent,
                IntPtr.Zero,
                string.IsNullOrWhiteSpace(cwd) ? null : cwd,
                ref startup,
                out var processInfo);

            Trace?.Invoke(
                $"CreateProcess ok={created} pid={processInfo.dwProcessId} " +
                $"lastError={Marshal.GetLastWin32Error()}");

            if (!created)
            {
                throw Fail("CreateProcess");
            }

            var reader = new FileStream(outputRead, FileAccess.Read, bufferSize: 1, isAsync: false);
            var writer = new FileStream(inputWrite, FileAccess.Write, bufferSize: 1, isAsync: false);

            return new ConPtyConnection(
                pc,
                processInfo.hProcess,
                processInfo.hThread,
                attributeList,
                processInfo.dwProcessId,
                reader,
                writer);
        }
        finally
        {
            Marshal.FreeHGlobal(commandLinePtr);
        }
    }

    private static IntPtr BuildAttributeList(IntPtr pseudoConsole)
    {
        nuint bytes = 0;
        var sized = ConPtyNative.InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref bytes);

        Trace?.Invoke(
            $"InitializeProcThreadAttributeList(size) ok={sized} bytes={bytes} " +
            $"lastError={Marshal.GetLastWin32Error()}");

        if (bytes == 0)
        {
            throw new InvalidOperationException(
                "InitializeProcThreadAttributeList reported a zero-byte attribute list");
        }

        var list = Marshal.AllocHGlobal((int)bytes);

        if (!ConPtyNative.InitializeProcThreadAttributeList(list, 1, 0, ref bytes))
        {
            Marshal.FreeHGlobal(list);
            throw Fail("InitializeProcThreadAttributeList");
        }

        var byPointer = Environment.GetEnvironmentVariable("PTYSPIKE_ATTR") == "pointer";

        var lpValue = pseudoConsole;

        if (byPointer)
        {
            lpValue = Marshal.AllocHGlobal(IntPtr.Size);
            Marshal.WriteIntPtr(lpValue, pseudoConsole);
        }

        var updated = ConPtyNative.UpdateProcThreadAttribute(
            list,
            0,
            ConPtyNative.ProcThreadAttributePseudoConsole,
            lpValue,
            (nuint)IntPtr.Size,
            IntPtr.Zero,
            IntPtr.Zero);

        Trace?.Invoke(
            $"UpdateProcThreadAttribute byPointer={byPointer} ok={updated} " +
            $"lastError={Marshal.GetLastWin32Error()}");

        if (!updated)
        {
            ConPtyNative.DeleteProcThreadAttributeList(list);
            Marshal.FreeHGlobal(list);
            throw Fail("UpdateProcThreadAttribute");
        }

        return list;
    }

    private static Win32Exception Fail(string what) =>
        new(Marshal.GetLastWin32Error(), $"{what} failed");
}
