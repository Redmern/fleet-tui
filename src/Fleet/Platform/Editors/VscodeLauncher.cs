using System.Diagnostics;
using Fleet.Ports.Editors;
using Fleet.Shared.Settings;

namespace Fleet.Platform.Editors;

public sealed class VscodeLauncher(Func<string, bool> onPath, Func<ProcessStartInfo, Process?> start) : IEditorLauncher
{
    private const string NewWindow = "-n";

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

    public string? TryStart(string flag, string target, bool windows, Action<Process>? started = null)
    {
        if (Build(flag, target, windows) is not { } info)
        {
            return $"VS Code cannot open {target}: on Windows a path with any of {string.Join(' ', CmdReinterprets)} would be run as a command. Rename it and try again.";
        }

        try
        {
            using var process = start(info);

            if (process is not null)
            {
                started?.Invoke(process);
            }

            return null;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return $"could not start {EditorChoice.VscodeCommand}: {e.Message}";
        }
    }

    public string? LaunchDetached(string folder) => TryStart(NewWindow, folder, OperatingSystem.IsWindows());
}
