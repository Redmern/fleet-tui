namespace Fleet.Shared.Orchestrations;

public static class ProjectConfigPaths
{
    public const string Folder = "config";

    public static string Root(string projectRoot) =>
        Path.Combine(projectRoot, OrchestrationPaths.Folder, Folder);

    public static string InstructionsFile(string projectRoot) =>
        Path.Combine(Root(projectRoot), "instructions.md");

    public static string ReadmeFile(string projectRoot) =>
        Path.Combine(Root(projectRoot), "README.md");
}
