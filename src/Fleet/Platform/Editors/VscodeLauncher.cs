using System.Diagnostics;
using Fleet.Ports.Editors;
using Fleet.Shared.Settings;

namespace Fleet.Platform.Editors;

public sealed class VscodeLauncher(Func<string, bool> onPath, Func<ProcessStartInfo, Process?> start) : IFileEditor
{
    private const string NewWindow = "-n";

    private const string Wait = "--wait";

    private static readonly char[] CmdReinterprets = ['&', '|', '<', '>', '^', '%', '!', '"'];

    public VscodeLauncher(Func<string, bool> onPath)
        : this(onPath, Process.Start)
    {
    }

    public bool Available => onPath(EditorChoice.VscodeCommand) || (OperatingSystem.IsWindows() && onPath(EditorChoice.VscodeCommand + ".cmd"));

    public static ProcessStartInfo? Build(string flag, string target, bool windows)
    {
        if (windows && target.IndexOfAny(CmdReinterprets) >= 0)
        {
            return null;
        }

        var info = windows
            ? new ProcessStartInfo("cmd") { ArgumentList = { "/d", "/c", EditorChoice.VscodeCommand, flag, target } }
            : new ProcessStartInfo("sh")
            {
                ArgumentList = { "-c", $"exec {EditorChoice.VscodeCommand} \"$@\" </dev/null >/dev/null 2>&1", "sh", flag, target },
            };

        info.UseShellExecute = false;
        info.CreateNoWindow = true;
        return info;
    }

    public string? TryStart(string flag, string target, bool windows, Action<Process>? started = null) =>
        Start(flag, target, windows, process =>
        {
            using (process)
            {
                if (process is not null)
                {
                    started?.Invoke(process);
                }
            }
        });

    public string? EditAndWait(string file, Action closed) => EditAndWait(file, closed, OperatingSystem.IsWindows());

    public string? EditAndWait(string file, Action closed, bool windows) =>
        Start(Wait, file, windows, process => _ = Task.Run(() => WaitThenAsync(process, closed)));

    private string? Start(string flag, string target, bool windows, Action<Process?> handle)
    {
        if (Build(flag, target, windows) is not { } info)
        {
            return $"VS Code cannot open {target}: on Windows a path with any of {string.Join(' ', CmdReinterprets)} would be run as a command. Rename it and try again.";
        }

        try
        {
            handle(start(info));
            return null;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return $"could not start {EditorChoice.VscodeCommand}: {e.Message}";
        }
    }

    private static async Task WaitThenAsync(Process? process, Action closed)
    {
        using (process)
        {
            try
            {
                if (process is not null)
                {
                    await process.WaitForExitAsync().ConfigureAwait(false);
                }
            }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
            }
        }

        closed();
    }

    public string? LaunchDetached(string folder) => TryStart(NewWindow, folder, OperatingSystem.IsWindows());
}
