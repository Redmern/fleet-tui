using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Fleet.Ports.Claude.Models;
using Fleet.Ports.Harness;
using Fleet.Shared.Harness;
using Fleet.Shared.Mcp;
using Fleet.Shared.Results;

namespace Fleet.Platform.Pi;

public sealed class PiHarnessConfig(
    Func<string, string, McpServerEntry> server,
    IReadOnlyDictionary<string, string> skills,
    Action<string, IReadOnlyList<string>> exclude,
    string trustStore) : IHarnessConfig
{
    public static readonly string ProjectConfig = Path.Combine(".pi", "mcp.json");

    public static readonly IReadOnlyList<string> Excludes =
        ["/.pi/mcp.json", "/.agents/", "/.fleet/", "/.fleet-ready"];

    private const string Servers = "mcpServers";

    private const int WaitsForPi = 5;

    private static readonly TimeSpan WaitForPi = TimeSpan.FromMilliseconds(20);

    private static readonly JsonSerializerOptions KeepAsWritten = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string TrustStoreIn(string? piAgentDir, string home) =>
        Path.Combine(AgentDirIn(piAgentDir, home), "trust.json");

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

    private static string AgentDirIn(string? piAgentDir, string home)
    {
        if (string.IsNullOrWhiteSpace(piAgentDir))
        {
            return Path.Combine(home, ".pi", "agent");
        }

        var given = piAgentDir.Trim();

        return given == "~" ? home
            : given.StartsWith("~/", StringComparison.Ordinal) ? Path.Combine(home, given[2..])
            : given;
    }

    private Result Write(string folder, string project, string caller)
    {
        var configured = Edit(
            Path.Combine(folder, ProjectConfig),
            text => WithServer(text ?? string.Empty, server(project, caller)));

        if (!configured.Succeeded)
        {
            return configured;
        }

        WriteSkills(folder);

        var trusted = PiTrustStore.Canonical(folder);

        return Edit(trustStore, text => PiTrustStore.WithTrusted(Settled(trustStore, text), trusted));
    }

    private static Result<string> WithServer(string text, McpServerEntry entry)
    {
        JsonObject? root;

        try
        {
            root = string.IsNullOrWhiteSpace(text) ? [] : JsonNode.Parse(text) as JsonObject;

            if (root is null)
            {
                return Result<string>.Fail("it is not a JSON object");
            }

            if (root[Servers] is null)
            {
                root[Servers] = new JsonObject();
            }

            if (root[Servers] is not JsonObject servers)
            {
                return Result<string>.Fail($"its {Servers} is not a JSON object");
            }

            var wanted = new JsonObject
            {
                ["command"] = entry.Command,
                ["args"] = new JsonArray([.. entry.Args.Select(argument => (JsonNode)JsonValue.Create(argument)!)]),
                ["exposure"] = "direct",
            };

            if (JsonNode.DeepEquals(servers[entry.Name], wanted))
            {
                return Result<string>.Ok(text);
            }

            servers[entry.Name] = wanted;

            return Result<string>.Ok(root.ToJsonString(KeepAsWritten) + Environment.NewLine);
        }
        catch (Exception e) when (e is JsonException or ArgumentException or InvalidOperationException)
        {
            return Result<string>.Fail("it is not JSON fleet can edit");
        }
    }

    private static string? Settled(string path, string? text)
    {
        for (var attempt = 0; attempt < WaitsForPi && text is not null && string.IsNullOrWhiteSpace(text); attempt++)
        {
            Thread.Sleep(WaitForPi);

            try
            {
                text = File.ReadAllText(path);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return text;
            }
        }

        return text;
    }

    private static Result Edit(string link, Func<string?, Result<string>> change)
    {
        string path;
        string? text;

        try
        {
            path = File.Exists(link)
                ? File.ResolveLinkTarget(link, returnFinalTarget: true)?.FullName ?? link
                : link;
            text = File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Result.Fail($"fleet could not read {link}: {e.Message}");
        }

        var changed = change(text);

        if (!changed.Succeeded)
        {
            return Result.Fail($"{link} was left untouched: {changed.Error}.");
        }

        if (changed.Value == text)
        {
            return Result.Ok();
        }

        var staged = $"{path}.{Guid.NewGuid():N}.fleet-tmp";

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
            return Result.Fail($"fleet could not write {link}: {e.Message}");
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
            var path = Path.Combine(folder, HarnessRegistry.Pi.SkillsDir, name, "SKILL.md");
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
