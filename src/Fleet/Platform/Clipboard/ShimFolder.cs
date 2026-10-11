using System.Diagnostics;

namespace Fleet.Platform.Clipboard;

public static class ShimFolder
{
    public const string RemoteDaemonVariable = "FLEET_REMOTE_DAEMON";

    public static string Default => Path.Combine(Fleet.Shared.FleetHome.Config, "shims");

    public static bool Install(string folder, IReadOnlyDictionary<string, string> scripts, Action<string> log)
    {
        if (OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            Directory.CreateDirectory(folder);
            foreach (var (tool, script) in scripts)
            {
                var file = Path.Combine(folder, tool);
                if (!File.Exists(file) || File.ReadAllText(file) != script)
                {
                    File.WriteAllText(file, script);
                }

                File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                                           | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                                           | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }

            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log($"clipboard shims: cannot write {folder}: {e.Message}");
            return false;
        }
    }

    public static async Task<int> RunRealToolAsync(string tool, string? real, IReadOnlyList<string> args)
    {
        if (real is null)
        {
            await Console.Error.WriteLineAsync($"fleet: {tool} is not installed here; fleet only serves its clipboard image reads")
                .ConfigureAwait(false);
            return 127;
        }

        var start = new ProcessStartInfo(real) { UseShellExecute = false };
        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        try
        {
            using var process = Process.Start(start);
            if (process is null)
            {
                return 127;
            }

            await process.WaitForExitAsync().ConfigureAwait(false);
            return process.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            await Console.Error.WriteLineAsync($"fleet: cannot run {real}: {e.Message}").ConfigureAwait(false);
            return 126;
        }
    }
}
