namespace Fleet.Features.Diagnostics.RunDoctor.Models;

public sealed record IncludeFacts(
    string Home,
    string IncludePath,
    string? IncludeText,
    IReadOnlyList<InstructionFile> ClaudeFiles,
    IReadOnlyList<InstructionFile> OpencodeConfigs,
    string CodexFile,
    string PiFile,
    IReadOnlyCollection<string> HarnessesInUse);
