using System.Diagnostics;
using Fleet.Platform.Mux.Embedded.Pty;

namespace Fleet.Platform.Mux.Embedded.Host;

public sealed record WindowLaunch(string Program, IReadOnlyList<string> Args, bool ShellExecute = false);

public static class NewWindow
{
    public static WindowLaunch? Plan(
        string executable,
        IReadOnlyList<string> args,
        IReadOnlyDictionary<string, string> env,
        Func<string, string?> variable,
        Func<string, bool> onPath,
        bool windows)
    {
        if (windows)
        {
            string[] command = env.Count == 0
                ? [executable, .. args]
                : WithEnv(executable, args, env);

            if (!string.IsNullOrEmpty(variable("WT_SESSION")) && onPath("wt"))
            {
                string[] profile = variable("WT_PROFILE_ID") is { Length: > 0 } id ? ["-p", id] : [];
                return new WindowLaunch("wt", ["-w", "new", .. profile, .. command]);
            }

            if (WezTerm(variable, onPath) is { } wezterm)
            {
                return new WindowLaunch(wezterm, [.. WezTermConfig(variable), "start", "--", .. command]);
            }

            return new WindowLaunch(command[0], command[1..], ShellExecute: true);
        }

        IReadOnlyList<string> unix = env.Count == 0
            ? [executable, .. args]
            : ["env", .. env.Select(e => $"{e.Key}={e.Value}"), executable, .. args];

        if (WezTerm(variable, onPath) is { } unixWezTerm)
        {
            return new WindowLaunch(unixWezTerm, [.. WezTermConfig(variable), "start", "--", .. unix]);
        }

        if (variable("TERMINAL") is { Length: > 0 } terminal && onPath(terminal))
        {
            return new WindowLaunch(terminal, ["-e", .. unix]);
        }

        return onPath("x-terminal-emulator") ? new WindowLaunch("x-terminal-emulator", ["-e", .. unix]) : null;
    }

    public static bool Open(WindowLaunch launch, Action<string> log)
    {
        try
        {
            var start = new ProcessStartInfo(launch.Program) { UseShellExecute = launch.ShellExecute };
            foreach (var arg in launch.Args)
            {
                start.ArgumentList.Add(arg);
            }

            using var process = Process.Start(start);
            log($"opened a window: {launch.Program} {string.Join(' ', launch.Args)}");
            return process is not null;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            log($"could not open a window with {launch.Program}: {e.Message}");
            return false;
        }
    }

    private static string? WezTerm(Func<string, string?> variable, Func<string, bool> onPath)
    {
        if (!string.Equals(variable("TERM_PROGRAM"), "WezTerm", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (variable("WEZTERM_EXECUTABLE") is { Length: > 0 } own
            && Path.GetFileNameWithoutExtension(own).StartsWith("wezterm", StringComparison.OrdinalIgnoreCase))
        {
            return own;
        }

        return onPath("wezterm") ? "wezterm" : null;
    }

    private static string[] WezTermConfig(Func<string, string?> variable) =>
        variable("WEZTERM_CONFIG_FILE") is { Length: > 0 } config ? ["--config-file", config] : [];

    private static string[] WithEnv(string executable, IReadOnlyList<string> args, IReadOnlyDictionary<string, string> env) =>
    [
        "cmd.exe", "/d", "/c",
        string.Concat(env.Select(e => $"set \"{e.Key}={e.Value}\"&& "))
            + WindowsCommandLine.For(executable, args),
    ];
}
