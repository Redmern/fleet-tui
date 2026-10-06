using Fleet.Shared.Constants;

namespace Fleet.Features.Head.ServeHead;

public static class HeadLaunch
{
    public const string VoiceFlag = "--voice";

    public const string HeadFlag = "--head";

    public const string VoiceOnFile = "voice-on.json";

    public const string VoiceOffFile = "voice-off.json";

    public const string StartedMarker = "started";

    public const string VoiceOn = """{"voice":{"enabled":true}}""";

    public const string VoiceOff = """{"voice":{"enabled":false}}""";

    public static IReadOnlyList<string> McpArgs { get; } = ["mcp", HeadFlag];

    public static IReadOnlyList<string> ClaudeArgs(string settingsFile, bool resume, ClaudeLaunch launch) =>
        [
            "--settings",
            settingsFile,
            .. resume ? [AgentHarness.ResumeArgument] : Array.Empty<string>(),
            .. launch.Arguments,
        ];

    public const string WindowsShell = "cmd.exe";

    public static string ShellArguments(string program, IReadOnlyList<string> args, Func<string, string> quote) =>
        $"/s /c \"{string.Join(' ', new[] { program }.Concat(args).Select(quote))}\"";
}
