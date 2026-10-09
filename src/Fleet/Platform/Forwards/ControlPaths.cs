using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace Fleet.Platform.Forwards;

public static class ControlPaths
{
    public const int Longest = 80;

    public const string Prefix = "cm-";

    public static bool Supported => !OperatingSystem.IsWindows();

    public static string Directory(Func<string, string?> env, string user)
    {
        if (env("XDG_RUNTIME_DIR") is { Length: > 0 } runtime)
        {
            var preferred = Path.Combine(runtime, "fleet");
            if (Fits(For(preferred, "x")))
            {
                return preferred;
            }
        }

        return Path.Combine("/tmp", $"fleet-{user}");
    }

    public static string Default() =>
        Directory(Environment.GetEnvironmentVariable, Environment.UserName);

    public static string For(string directory, string host) => Path.Combine(directory, Prefix + Hash(host));

    public static bool Fits(string path) => path.Length <= Longest;

    public static string Prepare(string directory)
    {
        System.IO.Directory.CreateDirectory(directory);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return directory;
    }

    public static IReadOnlyList<string> CleanStale(string directory, Func<string, bool> alive)
    {
        if (!System.IO.Directory.Exists(directory))
        {
            return [];
        }

        var removed = new List<string>();
        foreach (var socket in System.IO.Directory.EnumerateFiles(directory, Prefix + "*"))
        {
            if (alive(socket))
            {
                continue;
            }

            try
            {
                File.Delete(socket);
                removed.Add(socket);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }

        return removed;
    }

    public static bool Answers(string socket)
    {
        try
        {
            using var probe = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            probe.Connect(new UnixDomainSocketEndPoint(socket));
            return true;
        }
        catch (Exception e) when (e is SocketException or IOException or ArgumentException)
        {
            return false;
        }
    }

    private static string Hash(string host) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(host.ToLowerInvariant())))[..12];
}
