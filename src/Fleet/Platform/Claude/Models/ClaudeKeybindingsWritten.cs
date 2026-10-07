namespace Fleet.Platform.Claude.Models;

public sealed record ClaudeKeybindingsWritten(bool Changed, IReadOnlyList<ClaudeKeybinding> Conflicts);
