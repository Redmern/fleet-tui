using System.Diagnostics;
using Fleet.Shared.Constants;

namespace Fleet.Platform.Nvim;

public static class FleetNvimConfig
{
    private const string ResourcePrefix = "nvim/";

    private static readonly Lock Gate = new();

    private static bool _installed;

    public static string Directory => Path.Combine(ConfigHome, AgentHarness.FleetNvimAppName);

    private static string ConfigHome
    {
        get
        {
            var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");

            if (!string.IsNullOrWhiteSpace(xdg))
            {
                return xdg;
            }

            return OperatingSystem.IsWindows()
                ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        }
    }

    public static bool EnsureInstalled()
    {
        lock (Gate)
        {
            if (!_installed || !File.Exists(Path.Combine(Directory, "init.lua")))
            {
                _installed = Install();
            }

            return _installed;
        }
    }

    public static bool InstallPlugins()
    {
        try
        {
            var psi = new ProcessStartInfo(AgentHarness.Nvim)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("--headless");
            psi.ArgumentList.Add("+Lazy! install");
            psi.ArgumentList.Add("+qa");
            psi.Environment[AgentHarness.NvimAppNameVariable] = AgentHarness.FleetNvimAppName;

            using var process = Process.Start(psi);

            if (process is null)
            {
                return false;
            }

            _ = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(300_000))
            {
                process.Kill();
                return false;
            }

            return process.ExitCode == 0;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            return false;
        }
    }

    public static bool Install() => Install(Directory);

    public static bool Install(string target)
    {
        var assembly = typeof(FleetNvimConfig).Assembly;
        var names = assembly.GetManifestResourceNames()
            .Where(n => n.Replace('\\', '/').StartsWith(ResourcePrefix, StringComparison.Ordinal))
            .ToList();

        if (names.Count == 0)
        {
            return false;
        }

        try
        {
            foreach (var name in names)
            {
                using var stream = assembly.GetManifestResourceStream(name);

                if (stream is null)
                {
                    return false;
                }

                using var reader = new StreamReader(stream);
                var content = reader.ReadToEnd();
                var relative = name.Replace('\\', '/')[ResourcePrefix.Length..];
                var path = Path.Combine([target, .. relative.Split('/')]);

                if (File.Exists(path) && File.ReadAllText(path) == content)
                {
                    continue;
                }

                System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, content);
            }

            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return false;
        }
    }

    public static string? NvimVersionOutput()
    {
        try
        {
            var psi = new ProcessStartInfo(AgentHarness.Nvim)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("--version");

            using var process = Process.Start(psi);

            if (process is null)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEndAsync();

            if (!process.WaitForExit(5000))
            {
                process.Kill();
                return null;
            }

            return output.Result;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            return null;
        }
    }
}
