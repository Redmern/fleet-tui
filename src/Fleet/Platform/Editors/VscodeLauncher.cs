using System.Diagnostics;
using Fleet.Ports.Editors;
using Fleet.Shared.Settings;

namespace Fleet.Platform.Editors;

public sealed class VscodeLauncher(Func<string, bool> onPath, Func<ProcessStartInfo, Process?> start) : IEditorLauncher
{
    public VscodeLauncher(Func<string, bool> onPath)
        : this(onPath, Process.Start)
    {
    }

    public bool Available => onPath(EditorChoice.VscodeCommand) || (OperatingSystem.IsWindows() && onPath(EditorChoice.VscodeCommand + ".cmd"));

    public static ProcessStartInfo For(string folder, bool windows)
    {
        var info = windows
            ? new ProcessStartInfo("cmd") { ArgumentList = { "/c", EditorChoice.VscodeCommand, "-n", folder } }
            : new ProcessStartInfo("sh")
            {
                ArgumentList = { "-c", $"exec {EditorChoice.VscodeCommand} -n \"$1\" </dev/null >/dev/null 2>&1", "sh", folder },
            };

        info.UseShellExecute = false;
        info.CreateNoWindow = true;
        info.WorkingDirectory = folder;
        return info;
    }

    public string? LaunchDetached(string folder)
    {
        try
        {
            using var process = start(For(folder, OperatingSystem.IsWindows()));
            return null;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return $"could not start {EditorChoice.VscodeCommand}: {e.Message}";
        }
    }
}
