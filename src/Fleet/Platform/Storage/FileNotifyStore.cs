using System.Text;
using Fleet.Ports;

namespace Fleet.Platform.Storage;

public sealed class FileNotifyStore(IFleetLog? log = null, int attempts = 200) : INotifier
{
    public static string File => Path.Combine(FleetPaths.Requests, "notify.request");

    public void Notify(string message)
    {
        var line = message.Trim().Replace('\n', ' ').Replace('\r', ' ');

        if (line.Length == 0)
        {
            return;
        }

        for (var attempt = 0; attempt < attempts; attempt++)
        {
            try
            {
                Directory.CreateDirectory(FleetPaths.Requests);

                using var stream = new FileStream(File, FileMode.Append, FileAccess.Write, FileShare.None);
                stream.Write(Encoding.UTF8.GetBytes(line + "\n"));

                return;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(1);
            }
        }

        log?.Write($"notify dropped, the queue stayed locked: {line}");
    }

    public static IReadOnlyList<string> Drain()
    {
        try
        {
            using var stream = new FileStream(File, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
            var lines = reader.ReadToEnd().Split('\n').Where(l => l.Trim().Length > 0).ToList();
            stream.SetLength(0);

            return lines;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
