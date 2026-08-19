using System.Diagnostics;

namespace Fleet.Platform.Claude;

public static class ClaudeConfigHome
{
    public const string OverrideVariable = "CLAUDE_CONFIG_DIR";

    public static string ForFolder(string folder, string defaultHome)
    {
        var own = Environment.GetEnvironmentVariable(OverrideVariable);

        if (!string.IsNullOrWhiteSpace(own))
        {
            return own.Trim();
        }

        if (OperatingSystem.IsWindows())
        {
            var probed = ProbeWindows(folder);

            if (probed is { Length: > 0 })
            {
                return probed;
            }
        }

        return defaultHome;
    }

    private static string? ProbeWindows(string folder)
    {
        try
        {
            var start = new ProcessStartInfo("cmd.exe")
            {
                Arguments = "/c if defined CLAUDE_CONFIG_DIR echo %CLAUDE_CONFIG_DIR%",
                WorkingDirectory = Directory.Exists(folder) ? folder : Environment.CurrentDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(start);

            if (process is null)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEnd();

            process.WaitForExit(3000);

            var value = output.Trim();

            return value.Length == 0 ? null : value;
        }
        catch (Exception e)
            when (e is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            return null;
        }
    }
}
