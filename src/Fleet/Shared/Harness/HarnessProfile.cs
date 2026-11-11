namespace Fleet.Shared.Harness;

public sealed record HarnessProfile(
    string Name,
    string Binary,
    IReadOnlyList<string> ResumeArguments,
    IReadOnlyDictionary<string, string> SpawnEnv,
    string InstructionFile,
    HarnessCapabilities Capabilities,
    string SkillsDir)
{
    public bool TakesClaudeLaunch { get; init; }

    public string InstallHint { get; init; } = string.Empty;

    public bool Has(HarnessCapabilities capability) => (Capabilities & capability) == capability;

    public IReadOnlyList<string> Launch(IReadOnlyList<string> arguments) => [Binary, .. arguments];

    public bool Runs(IReadOnlyList<string> command) => command.Count > 0 && command[0] == Binary;

    public IReadOnlyList<string> Resumed(IReadOnlyList<string> command) =>
        !Runs(command) || ResumeArguments.Count == 0 || IsResumed(command)
            ? command
            : [Binary, .. ResumeArguments, .. command.Skip(1)];

    public string Kickoff => $"Read {InstructionFile} and TASK.md in your working directory, then begin.";

    private bool IsResumed(IReadOnlyList<string> command) => command.Contains(ResumeArguments[0]);
}
