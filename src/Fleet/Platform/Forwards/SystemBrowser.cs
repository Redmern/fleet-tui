using System.Diagnostics;
using Fleet.Ports.Browser;

namespace Fleet.Platform.Forwards;

public sealed class SystemBrowser(Func<ProcessStartInfo, Process?> start) : IBrowserLauncher
{
    public SystemBrowser()
        : this(Process.Start)
    {
    }

    public static ProcessStartInfo For(string url)
    {
        var info = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("cmd") { ArgumentList = { "/c", "start", string.Empty, url.Replace("&", "^&", StringComparison.Ordinal) } }
            : new ProcessStartInfo("sh")
            {
                ArgumentList = { "-c", $"exec {(OperatingSystem.IsMacOS() ? "open" : "xdg-open")} \"$1\" </dev/null >/dev/null 2>&1", "sh", url },
            };

        info.UseShellExecute = false;
        info.CreateNoWindow = true;
        return info;
    }

    public string? Open(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed) || parsed.Scheme is not ("http" or "https"))
        {
            return $"{url} is not an http address";
        }

        try
        {
            using var process = start(For(parsed.AbsoluteUri));
            return process is null ? $"could not open {url}" : null;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return $"could not open {url}: {e.Message}";
        }
    }
}
