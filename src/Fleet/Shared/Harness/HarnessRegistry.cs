using Fleet.Shared.Results;

namespace Fleet.Shared.Harness;

public static class HarnessRegistry
{
    public const string ClaudeName = "claude";

    public const string OpencodeName = "opencode";

    public const string CodexName = "codex";

    public const string EditorName = "nvim";

    public const string ResumeArgument = "--continue";

    private static readonly TimeSpan TuiInputRetry = TimeSpan.FromSeconds(1);

    public static HarnessProfile Claude { get; } = new(
        ClaudeName,
        ClaudeName,
        [ResumeArgument],
        new Dictionary<string, string>
        {
            ["CLAUDE_CODE_FORCE_SESSION_PERSISTENCE"] = "1",
            ["CLAUDE_CODE_CHILD_SESSION"] = string.Empty,
        },
        "CLAUDE.md",
        HarnessCapabilities.Mcp | HarnessCapabilities.Hooks | HarnessCapabilities.Skills
            | HarnessCapabilities.Permissions | HarnessCapabilities.NativeMessaging,
        Path.Combine(".claude", "skills"))
    {
        TakesClaudeLaunch = true,
        InstallHint = "npm install -g @anthropic-ai/claude-code",
    };

    public static HarnessProfile Opencode { get; } = new(
        OpencodeName,
        OpencodeName,
        [ResumeArgument],
        new Dictionary<string, string>(),
        "AGENTS.md",
        HarnessCapabilities.Mcp | HarnessCapabilities.Skills,
        Path.Combine(".agents", "skills"))
    {
        InstallHint = "npm install -g opencode-ai",
        InputRetry = TuiInputRetry,
    };

    public static HarnessProfile Codex { get; } = new(
        CodexName,
        CodexName,
        ["resume", "--last"],
        new Dictionary<string, string>(),
        "AGENTS.md",
        HarnessCapabilities.Mcp | HarnessCapabilities.Skills,
        Path.Combine(".agents", "skills"))
    {
        InputRetry = TuiInputRetry,
    };

    public static IReadOnlyList<HarnessProfile> Profiles { get; } = [Claude, Opencode, Codex];

    public static IReadOnlyList<string> Selectable { get; } =
        [EditorName, .. Profiles.Select(p => p.Name)];

    public static HarnessProfile? Find(string harness) =>
        Profiles.FirstOrDefault(p => p.Name == harness.Trim());

    public static HarnessProfile? Running(IReadOnlyList<string> command) =>
        Profiles.FirstOrDefault(p => p.Runs(command));

    public static HarnessProfile For(string harness) => Find(harness) ?? Claude;

    public static Result<string> Require(string harness)
    {
        var wanted = harness.Trim();

        return Selectable.Contains(wanted)
            ? Result<string>.Ok(wanted)
            : Result<string>.Fail(
                $"'{wanted}' is not a registered harness. Valid harnesses: {string.Join(", ", Selectable)}.");
    }
}
