using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Fleet.Platform.Harness;
using Fleet.Ports.Claude.Models;
using Fleet.Ports.Harness;
using Fleet.Shared.Harness;
using Fleet.Shared.Mcp;
using Fleet.Shared.Results;

namespace Fleet.Platform.Opencode;

public sealed class OpencodeHarnessConfig(
    Func<string, string, McpServerEntry> server,
    IReadOnlyDictionary<string, string> skills,
    Action<string, IReadOnlyList<string>> exclude) : IHarnessConfig
{
    public const string ConfigFile = "opencode.json";

    public static readonly IReadOnlyList<string> Excludes =
        ["/" + ConfigFile, "/.agents/", "/.fleet/", "/.fleet-ready"];

    private static readonly JsonSerializerOptions KeepAsWritten = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public Result WriteForOrchestration(string folder, string project, string caller) =>
        Write(folder, project, caller);

    public Result WriteForAgent(string folder, string project, string repository, string branch)
    {
        var written = Write(folder, project, McpCaller.ForAgent(repository, branch));

        if (written.Succeeded)
        {
            exclude(folder, Excludes);
        }

        return written;
    }

    private Result Write(string folder, string project, string caller)
    {
        var configured = WriteServer(Path.Combine(folder, ConfigFile), server(project, caller));

        if (configured.Succeeded)
        {
            HarnessFiles.WriteSkills(folder, HarnessRegistry.Opencode.SkillsDir, skills);
        }

        return configured;
    }

    private static readonly JsonDocumentOptions Jsonc = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static Result WriteServer(string path, McpServerEntry entry)
    {
        JsonObject? root;

        try
        {
            var text = File.Exists(path) ? File.ReadAllText(path) : string.Empty;
            root = string.IsNullOrWhiteSpace(text)
                ? new JsonObject { ["$schema"] = "https://opencode.ai/config.json" }
                : JsonNode.Parse(text, documentOptions: Jsonc) as JsonObject;
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            root = null;
        }

        if (root is null)
        {
            return Result.Fail($"{path} could not be read as JSON; fleet left it untouched.");
        }

        if (root["mcp"] is not JsonObject servers)
        {
            servers = [];
            root["mcp"] = servers;
        }

        var command = new JsonArray(JsonValue.Create(entry.Command));

        foreach (var argument in entry.Args)
        {
            command.Add((JsonNode)JsonValue.Create(argument));
        }

        servers[entry.Name] = new JsonObject
        {
            ["type"] = "local",
            ["command"] = command,
            ["enabled"] = true,
        };

        try
        {
            File.WriteAllText(path, root.ToJsonString(KeepAsWritten) + Environment.NewLine);
            return Result.Ok();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Result.Fail($"fleet could not write {path}: {e.Message}");
        }
    }
}
