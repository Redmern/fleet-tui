using System.Diagnostics;

namespace Fleet.Platform.Sync;

public static class SyncSpawn
{
    public const string RemoteCommandVariable = "FLEET_REMOTE_COMMAND";

    public static ProcessStartInfo Bridge(string host)
    {
        var start = new ProcessStartInfo("ssh")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var arg in (string[])["-T", "-o", "ConnectTimeout=15", host, Environment.GetEnvironmentVariable(RemoteCommandVariable) ?? "fleet", "bridge"])
        {
            start.ArgumentList.Add(arg);
        }

        return start;
    }

    public static Process Start(ISyncProcessRunner runner, string host) => runner.Start(Bridge(host));
}
