using System.Diagnostics;
using Fleet.Ports.Browser;

namespace Fleet.Platform.Forwards;

public sealed class SystemBrowser(Func<ProcessStartInfo, Process?> start, TimeSpan exitWithin) : IBrowserLauncher
{
    public static readonly TimeSpan OpenerExitWithin = TimeSpan.FromSeconds(2);

    public SystemBrowser()
        : this(Process.Start, OpenerExitWithin)
    {
    }

    public static ProcessStartInfo For(string url)
    {
        var info = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("cmd") { ArgumentList = { "/c", "start", string.Empty, url.Replace("&", "^&", StringComparison.Ordinal) } }
            : new ProcessStartInfo("sh")
            {
                ArgumentList = { "-c", $"exec {Opener} \"$1\" </dev/null >/dev/null 2>&1", "sh", url },
            };

        info.UseShellExecute = false;
        info.CreateNoWindow = true;
        return info;
    }

    private static string Opener =>
        OperatingSystem.IsWindows() ? "start" : OperatingSystem.IsMacOS() ? "open" : "xdg-open";

    public string? Open(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed) || parsed.Scheme is not ("http" or "https"))
        {
            return $"{url} is not an http address";
        }

        try
        {
            using var process = start(For(parsed.AbsoluteUri));
            if (process is null)
            {
                return $"could not open {url}";
            }

            return process.WaitForExit(exitWithin) && process.ExitCode != 0
                ? $"could not open {url}: {Opener} failed with exit code {process.ExitCode}"
                : null;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return $"could not open {url}: {e.Message}";
        }
    }
}
