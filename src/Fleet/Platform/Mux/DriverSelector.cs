namespace Fleet.Platform.Mux;

/// <summary>
/// Everything driver selection depends on, gathered up so the decision itself
/// stays a pure function and can be tested without touching the environment.
/// </summary>
public sealed record MuxEnvironment
{
    public string? Override { get; init; }

    public bool InsideTmux { get; init; }

    public bool InsideWezTerm { get; init; }

    public bool GuiReachable { get; init; }

    public IReadOnlySet<string> Installed { get; init; } = new HashSet<string>();

    /// <summary>
    /// Reads the real environment.
    ///
    /// GUI reachability is probed rather than inferred from the operating system:
    /// Linux needs a display server, and a Windows SSH session has no visible
    /// desktop. That is what makes "SSH into the Linux box falls back to tmux"
    /// work with no configuration and no OS sniffing — neither display variable
    /// survives the hop.
    /// </summary>
    public static MuxEnvironment Current(Func<string, bool> isInstalled) => new()
    {
        Override = Environment.GetEnvironmentVariable("FLEET_MUX"),
        InsideTmux = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TMUX")),
        InsideWezTerm = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WEZTERM_PANE")),
        GuiReachable = OperatingSystem.IsWindows()
            ? string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SSH_CONNECTION"))
            : !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"))
              || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")),
        Installed = new HashSet<string>(
            new[] { DriverSelector.WezTerm, DriverSelector.Tmux }.Where(isInstalled),
            StringComparer.OrdinalIgnoreCase),
    };

    /// <summary>Is an executable on PATH? Tolerates malformed PATH entries.</summary>
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
                    return false;   // a PATH entry with invalid path characters
                }
            });
    }
}

public static class DriverSelector
{
    public const string WezTerm = "wezterm";
    public const string Tmux = "tmux";
    public const string Embedded = "embedded";

    /// <summary>
    /// Two separable decisions, often conflated.
    ///
    /// <b>Adopt</b> — if fleet starts inside an existing multiplexer it must use
    /// that one whatever the preference order says, or it would spawn WezTerm tabs
    /// while the user sits in a tmux pane. This is correctness, not taste.
    ///
    /// <b>Launch</b> — only when nothing is around fleet does preference apply:
    /// WezTerm is the base, tmux the fallback, embedded the last resort.
    /// </summary>
    public static string Choose(MuxEnvironment env)
    {
        if (!string.IsNullOrWhiteSpace(env.Override))
        {
            return env.Override.Trim().ToLowerInvariant();
        }

        if (env.InsideTmux)
        {
            return Tmux;
        }

        if (env.InsideWezTerm)
        {
            return WezTerm;
        }

        if (env.GuiReachable && env.Installed.Contains(WezTerm))
        {
            return WezTerm;
        }

        return env.Installed.Contains(Tmux) ? Tmux : Embedded;
    }
}
