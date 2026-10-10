namespace Fleet.Platform.Forwards;

public static class SshControl
{
    public const string Loopback = "127.0.0.1";

    public static IReadOnlyList<string> MasterOptions(string controlPath) =>
    [
        "-o", "ControlMaster=yes",
        "-o", $"ControlPath={controlPath}",
        "-o", "ControlPersist=no",
        "-o", "ExitOnForwardFailure=yes",
        "-o", "ServerAliveInterval=15",
        "-o", "ServerAliveCountMax=3",
    ];

    public static string Spec(int local, string target, int remote) => $"{Loopback}:{local}:{target}:{remote}";

    public static IReadOnlyList<string> Forward(string controlPath, string host, int local, string target, int remote) =>
        ["-S", controlPath, "-O", "forward", "-L", Spec(local, target, remote), host];

    public static IReadOnlyList<string> Cancel(string controlPath, string host, int local, string target, int remote) =>
        ["-S", controlPath, "-O", "cancel", "-L", Spec(local, target, remote), host];

    public static IReadOnlyList<string> Check(string controlPath, string host) =>
        ["-S", controlPath, "-O", "check", host];

    public static IReadOnlyList<string> Exit(string controlPath, string host) =>
        ["-S", controlPath, "-O", "exit", host];

    public static IReadOnlyList<string> Run(string controlPath, string host, string command) =>
        ["-S", controlPath, "-o", "ControlMaster=no", "-o", "BatchMode=yes", "-T", host, command];

    public static bool Prohibited(string text) =>
        text.Contains("administratively prohibited", StringComparison.OrdinalIgnoreCase);
}
