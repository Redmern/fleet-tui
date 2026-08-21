using Fleet.Ports;

namespace Fleet.Platform.Storage;

public sealed class FileNotifyStore : INotifier
{
    public static string File => Path.Combine(FleetPaths.Requests, "notify.request");

    public void Notify(string message)
    {
        var line = message.Trim().Replace('\n', ' ').Replace('\r', ' ');

        if (line.Length == 0)
        {
            return;
        }

        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                Directory.CreateDirectory(FleetPaths.Requests);
                System.IO.File.AppendAllText(File, line + "\n");
                return;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(20);
            }
        }
    }
}
