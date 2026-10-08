using Fleet.Platform.Mux.Constants;
using Fleet.Platform.Mux.Embedded.Native;

namespace Fleet.Platform.Mux.Models;

public sealed record MuxEnvironment
{
    public string? Override { get; init; }

    public bool InsideTmux { get; init; }

    public IReadOnlySet<string> Installed { get; init; } = new HashSet<string>();

    public bool EmbeddedReady { get; init; }

    public static MuxEnvironment Current(Func<string, bool> isInstalled) => new()
    {
        EmbeddedReady = GhosttyNative.Available(),
        Override = Environment.GetEnvironmentVariable("FLEET_MUX"),
        InsideTmux = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TMUX")),
        Installed = new HashSet<string>(
            new[] { DriverNames.Tmux }.Where(isInstalled),
            StringComparer.OrdinalIgnoreCase),
    };

    public static bool OnPath(string exe)
    {
        var names = OperatingSystem.IsWindows() ? [exe + ".exe", exe] : new[] { exe };

        return (Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator) ?? [])
            .Any(dir =>
            {
                if (string.IsNullOrWhiteSpace(dir))
                {
                    return false;
                }

                try
                {
                    return names.Any(n => File.Exists(Path.Combine(dir, n)));
                }
                catch (ArgumentException)
                {
                    return false;
                }
            });
    }
}
