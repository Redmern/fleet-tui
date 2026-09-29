using System.ComponentModel;
using System.Diagnostics;
using System.Security;
using System.Text;

namespace Fleet.Platform.Notifications;

public static class DesktopToast
{
    private const string PowerShellAppId = @"{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\WindowsPowerShell\v1.0\powershell.exe";

    public static bool Show(string title, string body)
    {
        try
        {
            var start = OperatingSystem.IsWindows() ? Windows(title, body) : Unix(title, body);
            using var process = Process.Start(start);
            return process is not null;
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            return false;
        }
    }

    public static string ToastXml(string title, string body) =>
        $"<toast><visual><binding template=\"ToastGeneric\"><text>{SecurityElement.Escape(title)}</text><text>{SecurityElement.Escape(body)}</text></binding></visual></toast>";

    private static ProcessStartInfo Windows(string title, string body)
    {
        var script = string.Join('\n',
            "[Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] > $null",
            "[Windows.Data.Xml.Dom.XmlDocument, Windows.Data.Xml.Dom.XmlDocument, ContentType = WindowsRuntime] > $null",
            "$xml = New-Object Windows.Data.Xml.Dom.XmlDocument",
            $"$xml.LoadXml('{ToastXml(title, body).Replace("'", "''", StringComparison.Ordinal)}')",
            $"[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('{PowerShellAppId}').Show([Windows.UI.Notifications.ToastNotification]::new($xml))");

        var start = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var arg in (string[])["-NoProfile", "-NonInteractive", "-WindowStyle", "Hidden", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script))])
        {
            start.ArgumentList.Add(arg);
        }

        return start;
    }

    private static ProcessStartInfo Unix(string title, string body)
    {
        var start = new ProcessStartInfo("notify-send")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        start.ArgumentList.Add("--app-name=fleet");
        start.ArgumentList.Add(title);
        start.ArgumentList.Add(body);
        return start;
    }
}
