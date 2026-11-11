using Fleet.Platform.Harness;
using Fleet.Ports.Claude.Models;
using Fleet.Ports.Harness;
using Fleet.Shared.Harness;
using Fleet.Shared.Mcp;
using Fleet.Shared.Results;

namespace Fleet.Platform.Codex;

public sealed class CodexHarnessConfig(
    Func<string, string, McpServerEntry> server,
    IReadOnlyDictionary<string, string> skills,
    Action<string, IReadOnlyList<string>> exclude,
    string userConfig) : IHarnessConfig
{
    public static readonly string ProjectConfig = Path.Combine(".codex", "config.toml");

    public static readonly IReadOnlyList<string> Excludes =
        ["/.codex/config.toml", "/.agents/", "/.fleet/", "/.fleet-ready"];

    public static string UserConfigIn(string? codexHome, string home) =>
        Path.Combine(string.IsNullOrWhiteSpace(codexHome) ? Path.Combine(home, ".codex") : codexHome, "config.toml");

    public Result WriteForOrchestration(string folder, string project, string caller) =>
        Write(folder, project, caller);

    public Result WriteForAgent(string folder, string project, string repository, string branch)
    {
        var written = Write(folder, project, McpCaller.ForAgent(repository, branch));

        if (File.Exists(Path.Combine(folder, ProjectConfig)))
        {
            exclude(folder, Excludes);
        }

        return written;
    }

    private Result Write(string folder, string project, string caller)
    {
        var configured = HarnessFiles.Edit(
            Path.Combine(folder, ProjectConfig),
            text => CodexToml.WithServer(text ?? string.Empty, server(project, caller)));

        if (!configured.Succeeded)
        {
            return configured;
        }

        HarnessFiles.WriteSkills(folder, HarnessRegistry.Codex.SkillsDir, skills);

        return HarnessFiles.Edit(userConfig, text => CodexToml.WithTrustedProject(text ?? string.Empty, Path.GetFullPath(folder)));
    }
}
