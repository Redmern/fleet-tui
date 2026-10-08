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

            return new WindowLaunch(command[0], command[1..], ShellExecute: true);
        }

        IReadOnlyList<string> unix = env.Count == 0
            ? [executable, .. args]
            : ["env", .. env.Select(e => $"{e.Key}={e.Value}"), executable, .. args];

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

    private static string[] WithEnv(string executable, IReadOnlyList<string> args, IReadOnlyDictionary<string, string> env) =>
    [
        "cmd.exe", "/d", "/c",
        string.Concat(env.Select(e => $"set \"{e.Key}={e.Value}\"&& "))
            + WindowsCommandLine.For(executable, args),
    ];
}
