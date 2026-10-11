using System.Security.Cryptography;
using System.Text;

namespace Fleet.Platform.Forwards;

public static class ReverseForward
{
    public const string OptInVariable = "FLEET_VOICE_FORWARD";

    public const string PulseVariable = "PULSE_SERVER";

    public const string Prefix = "pulse-";

    public const string Suffix = ".sock";

    private const string Folder = "fleet-pulse";

    private const string UnixScheme = "unix:";

    public static bool OptedIn(Func<string, string?> env) =>
        env(OptInVariable) is { } value
        && (value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase) || value.Equals("yes", StringComparison.OrdinalIgnoreCase));

    public static string? LocalPulse(Func<string, string?> env, Func<string, bool> exists)
    {
        string? socket = env(PulseVariable) is { Length: > 0 } server
            ? server.StartsWith(UnixScheme, StringComparison.Ordinal) ? server[UnixScheme.Length..] : null
            : env("XDG_RUNTIME_DIR") is { Length: > 0 } runtime ? Path.Combine(runtime, "pulse", "native") : null;

        return socket is { Length: > 0 } && exists(socket) ? socket : null;
    }

    public static string SocketName(string machine, string user) =>
        Prefix + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{machine.ToLowerInvariant()}\n{user}")))[..12] + Suffix;

    public static string Directory(Func<string, string?> env, string user) =>
        env("XDG_RUNTIME_DIR") is { Length: > 0 } runtime && Path.Combine(runtime, Folder) is var preferred && ControlPaths.Fits(preferred)
            ? preferred
            : Path.Combine("/tmp", $"{Folder}-{user}");

    public static string Default() => Directory(Environment.GetEnvironmentVariable, Environment.UserName);

    public static string Prepare(string name) =>
        "sh -c '"
        + $"d=\"${{XDG_RUNTIME_DIR:+$XDG_RUNTIME_DIR/{Folder}}}\"; "
        + $"if [ -z \"$d\" ] || [ ${{#d}} -gt {ControlPaths.Longest} ]; then d=\"/tmp/{Folder}-$(id -un)\"; fi; "
        + $"mkdir -p \"$d\" && chmod 700 \"$d\" && rm -f \"$d/{name}\" && printf \"%s\\n\" \"$d\""
        + "'";

    public static void Apply(IDictionary<string, string> env, string directory, Func<string, bool> alive)
    {
        if (Newest(directory, alive) is { } socket)
        {
            env[PulseVariable] = UnixScheme + socket;
        }
    }

    private static string? Newest(string directory, Func<string, bool> alive)
    {
        try
        {
            return new DirectoryInfo(directory).Exists
                ? new DirectoryInfo(directory)
                    .EnumerateFiles(Prefix + "*" + Suffix)
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .Select(f => f.FullName)
                    .FirstOrDefault(alive)
                : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
