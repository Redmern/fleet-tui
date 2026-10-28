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
        var configured = Edit(
            Path.Combine(folder, ProjectConfig),
            text => CodexToml.WithServer(text, server(project, caller)));

        if (!configured.Succeeded)
        {
            return configured;
        }

        WriteSkills(folder);

        return Edit(userConfig, text => CodexToml.WithTrustedProject(text, Path.GetFullPath(folder)));
    }

    private static Result Edit(string path, Func<string, Result<string>> change)
    {
        string text;

        try
        {
            text = File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Result.Fail($"fleet could not read {path}: {e.Message}");
        }

        var changed = change(text);

        if (!changed.Succeeded)
        {
            return Result.Fail($"{path} was left untouched: {changed.Error}.");
        }

        if (changed.Value == text && File.Exists(path))
        {
            return Result.Ok();
        }

        var staged = path + ".fleet-tmp";

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(staged, changed.Value);
            File.Move(staged, path, overwrite: true);
            return Result.Ok();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Discard(staged);
            return Result.Fail($"fleet could not write {path}: {e.Message}");
        }
    }

    private static void Discard(string staged)
    {
        try
        {
            File.Delete(staged);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private void WriteSkills(string folder)
    {
        foreach (var (name, content) in skills)
        {
            var path = Path.Combine(folder, HarnessRegistry.Codex.SkillsDir, name, "SKILL.md");
            var text = content.Replace("\r\n", "\n", StringComparison.Ordinal);

            try
            {
                if (File.Exists(path) && File.ReadAllText(path) == text)
                {
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, text);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
