using Fleet.Platform.Mux.Constants;
using Fleet.Platform.Mux.Embedded.Native;

namespace Fleet.Platform.Mux.Models;

public sealed record MuxEnvironment
{
    public string? Override { get; init; }

    public bool InsideTmux { get; init; }

    public bool InsideWezTerm { get; init; }

    public bool GuiReachable { get; init; }

    public IReadOnlySet<string> Installed { get; init; } = new HashSet<string>();

    public bool EmbeddedReady { get; init; }

    public static MuxEnvironment Current(Func<string, bool> isInstalled) => new()
    {
        EmbeddedReady = GhosttyNative.Available(),
        Override = Environment.GetEnvironmentVariable("FLEET_MUX"),
        InsideTmux = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TMUX")),
        InsideWezTerm = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WEZTERM_PANE")),
        GuiReachable = OperatingSystem.IsWindows()
            ? string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SSH_CONNECTION"))
            : !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"))
              || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")),
        Installed = new HashSet<string>(
            new[] { DriverNames.WezTerm, DriverNames.Tmux }.Where(isInstalled),
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
