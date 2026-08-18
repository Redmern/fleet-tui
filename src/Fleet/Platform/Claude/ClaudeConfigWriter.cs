using System.Text.Json;
using Fleet.Platform.Claude.Models;
using Fleet.Ports.Claude;
using Fleet.Ports.Claude.Models;
using Fleet.Shared.Results;

namespace Fleet.Platform.Claude;

public sealed class ClaudeConfigWriter : IClaudeConfigStore
{
    public Result Sync(ClaudePlan plan)
    {
        var mcpPath = Path.Combine(plan.Directory, ".mcp.json");
        var settingsPath = Path.Combine(plan.Directory, ".claude", "settings.local.json");

        var mcp = ReadMcp(mcpPath);

        if (mcp is null)
        {
            return Result.Fail($"{mcpPath} is not valid JSON; fleet left it untouched.");
        }

        var settings = ReadSettings(settingsPath);

        if (settings is null)
        {
            return Result.Fail($"{settingsPath} is not valid JSON; fleet left it untouched.");
        }

        Apply(mcp, plan.Server);
        Apply(settings, plan);

        if (!Write(mcpPath, JsonSerializer.Serialize(mcp, ClaudeJsonContext.Default.McpJsonFile)))
        {
            return Result.Fail($"fleet could not write {mcpPath}.");
        }

        if (!Write(
            settingsPath, JsonSerializer.Serialize(settings, ClaudeJsonContext.Default.ClaudeSettingsFile)))
        {
            return Result.Fail($"fleet could not write {settingsPath}.");
        }

        return Result.Ok();
    }

    public Result EnableServer(string userSettingsPath, string serverName)
    {
        var file = Read(
            userSettingsPath, ClaudeJsonContext.Default.UserSettingsFile, () => new UserSettingsFile());

        if (file is null)
        {
            return Result.Fail($"{userSettingsPath} is not valid JSON; fleet left it untouched.");
        }

        if (file.EnabledMcpjsonServers.Contains(serverName))
        {
            return Result.Ok();
        }

        file.EnabledMcpjsonServers.Add(serverName);

        return Write(userSettingsPath, JsonSerializer.Serialize(file, ClaudeJsonContext.Default.UserSettingsFile))
            ? Result.Ok()
            : Result.Fail($"fleet could not write {userSettingsPath}.");
    }

    public Result SyncWorktree(McpServerEntry server, string directory, IReadOnlyList<string> allow)
    {
        var mcpPath = Path.Combine(directory, ".mcp.json");
        var settingsPath = Path.Combine(directory, ".claude", "settings.local.json");

        var mcp = ReadMcp(mcpPath);
        var settings = ReadSettings(settingsPath);

        if (mcp is null)
        {
            return Result.Fail($"{mcpPath} is not valid JSON; fleet left it untouched.");
        }

        if (settings is null)
        {
            return Result.Fail($"{settingsPath} is not valid JSON; fleet left it untouched.");
        }

        Apply(mcp, server);

        if (!settings.EnabledMcpjsonServers.Contains(server.Name))
        {
            settings.EnabledMcpjsonServers.Add(server.Name);
        }

        settings.EnableAllProjectMcpServers = true;
        settings.Permissions.Allow = Merge(settings.Permissions.Allow, allow);

        if (!Write(mcpPath, JsonSerializer.Serialize(mcp, ClaudeJsonContext.Default.McpJsonFile)))
        {
            return Result.Fail($"fleet could not write {mcpPath}.");
        }

        return Write(
            settingsPath, JsonSerializer.Serialize(settings, ClaudeJsonContext.Default.ClaudeSettingsFile))
            ? Result.Ok()
            : Result.Fail($"fleet could not write {settingsPath}.");
    }

    public Result TrustFolder(string claudeJsonPath, string folder)
    {
        var file = Read(
            claudeJsonPath, ClaudeJsonContext.Default.ClaudeGlobalFile, () => new ClaudeGlobalFile());

        if (file is null)
        {
            return Result.Fail($"{claudeJsonPath} is not valid JSON; fleet left it untouched.");
        }

        var key = Path.GetFullPath(folder).Replace('\\', '/');

        if (!file.Projects.TryGetValue(key, out var entry))
        {
            entry = new ClaudeProjectEntry();
            file.Projects[key] = entry;
        }
        else if (entry.HasTrustDialogAccepted == true)
        {
            return Result.Ok();
        }

        entry.HasTrustDialogAccepted = true;

        return Write(claudeJsonPath, JsonSerializer.Serialize(file, ClaudeJsonContext.Default.ClaudeGlobalFile))
            ? Result.Ok()
            : Result.Fail($"fleet could not write {claudeJsonPath}.");
    }

    public ClaudeState Inspect(string directory, string serverName)
    {
        var mcp = ReadMcp(Path.Combine(directory, ".mcp.json"));
        var settings = ReadSettings(Path.Combine(directory, ".claude", "settings.local.json"));

        if (mcp is null || settings is null)
        {
            return ClaudeState.Absent;
        }

        var hookInstalled = settings.Hooks?.UserPromptSubmit
            .Any(group => group.Hooks.Any(IsOwnedHook)) ?? false;

        return new ClaudeState(
            mcp.McpServers.ContainsKey(serverName),
            settings.EnabledMcpjsonServers.Contains(serverName),
            hookInstalled,
            settings.Permissions.Allow);
    }

    private static void Apply(McpJsonFile file, McpServerEntry server)
    {
        if (!file.McpServers.TryGetValue(server.Name, out var entry))
        {
            entry = new McpServerJson();
            file.McpServers[server.Name] = entry;
        }

        entry.Type = "stdio";
        entry.Command = server.Command;
        entry.Args = [.. server.Args];
    }

    private static void Apply(ClaudeSettingsFile file, ClaudePlan plan)
    {
        file.Permissions.Allow = Merge(file.Permissions.Allow, plan.Allow);
        file.Permissions.Deny = Merge(file.Permissions.Deny, plan.Deny);
        file.Permissions.Ask = Merge(file.Permissions.Ask, plan.Ask);

        if (!file.EnabledMcpjsonServers.Contains(plan.Server.Name))
        {
            file.EnabledMcpjsonServers.Add(plan.Server.Name);
        }

        ApplyHook(file, plan.HookCommand, plan.HookArgs);
    }

    private static void ApplyHook(
        ClaudeSettingsFile file, string command, IReadOnlyList<string> args)
    {
        if (command.Trim().Length == 0)
        {
            return;
        }

        var hooks = file.Hooks ??= new HooksJson();

        hooks.UserPromptSubmit = hooks.UserPromptSubmit
            .Where(group => !group.Hooks.Any(IsOwnedHook))
            .ToList();

        hooks.UserPromptSubmit.Add(new HookGroup
        {
            Hooks = [new HookEntry { Type = "command", Command = command, Args = [.. args] }],
        });
    }

    private static bool IsOwnedHook(HookEntry entry) =>
        entry.Command.Contains("hook-dispatch", StringComparison.Ordinal)
        || (entry.Args?.Contains("hook-dispatch") ?? false);

    private static List<string> Merge(List<string> existing, IReadOnlyList<string> owned)
    {
        var kept = existing.Where(e => !IsOwned(e)).ToList();

        kept.AddRange(owned);

        return kept;
    }

    private static bool IsOwned(string rule) =>
        rule.StartsWith("mcp__fleet__", StringComparison.Ordinal)
        || rule.Equals("mcp__fleet", StringComparison.Ordinal);

    private static McpJsonFile? ReadMcp(string path) =>
        Read(path, ClaudeJsonContext.Default.McpJsonFile, () => new McpJsonFile());

    private static ClaudeSettingsFile? ReadSettings(string path) =>
        Read(path, ClaudeJsonContext.Default.ClaudeSettingsFile, () => new ClaudeSettingsFile());

    private static T? Read<T>(
        string path, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> info, Func<T> empty)
        where T : class
    {
        try
        {
            if (!File.Exists(path))
            {
                return empty();
            }

            var text = File.ReadAllText(path);

            if (string.IsNullOrWhiteSpace(text))
            {
                return empty();
            }

            return JsonSerializer.Deserialize(text, info) ?? empty();
        }
        catch (JsonException)
        {
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return empty();
        }
    }

    private static bool Write(string path, string content)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);

            if (directory is { Length: > 0 })
            {
                Directory.CreateDirectory(directory);
            }

            var temp = path + ".tmp";

            File.WriteAllText(temp, content);
            File.Move(temp, path, overwrite: true);

            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
