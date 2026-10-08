namespace Fleet.Shared.Orchestrations;

public static class ProjectConfigPaths
{
    public const string Folder = "config";

    public static string Root(string projectRoot) =>
        Path.Combine(projectRoot, OrchestrationPaths.Folder, Folder);

    public static string InstructionsFile(string projectRoot) =>
        Path.Combine(Root(projectRoot), "instructions.md");

    public static string AiDlcFile(string projectRoot) =>
        Path.Combine(Root(projectRoot), "ai-dlc.md");

    public static string AidlcFile(string projectRoot) =>
        Path.Combine(Root(projectRoot), "aidlc.md");

    public static string ProcessFile(string projectRoot) =>
        File.Exists(AiDlcFile(projectRoot)) ? AiDlcFile(projectRoot) : AidlcFile(projectRoot);

    public static string ReadmeFile(string projectRoot) =>
        Path.Combine(Root(projectRoot), "README.md");
}
