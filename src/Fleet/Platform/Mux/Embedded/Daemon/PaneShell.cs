namespace Fleet.Platform.Mux.Embedded.Daemon;

public static class PaneShell
{
    public const string Variable = "FLEET_SHELL";

    public static IReadOnlyList<string> Current() =>
        For(Environment.GetEnvironmentVariable, Models.MuxEnvironment.OnPath, OperatingSystem.IsWindows());

    public static IReadOnlyList<string> For(Func<string, string?> env, Func<string, bool> onPath, bool windows)
    {
        if (env(Variable) is { Length: > 0 } chosen && chosen.Trim().Length > 0)
        {
            return [chosen.Trim()];
        }

        if (windows)
        {
            return onPath("pwsh") ? ["pwsh.exe", "-NoLogo"] : [env("COMSPEC") ?? "cmd.exe"];
        }

        return [env("SHELL") is { Length: > 0 } shell ? shell : "/bin/sh"];
    }
}
