using System.Text.Json;
using Fleet.Platform.Claude.Models;
using Fleet.Platform.Storage;
using Fleet.Ports.Claude;
using Fleet.Ports.Claude.Models;
using Fleet.Shared.Hooks;
using Fleet.Shared.Results;
using Fleet.Shared.Settings;

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
            return Result.Fail($"{mcpPath} could not be read as JSON; fleet left it untouched.");
        }

        var settings = ReadSettings(settingsPath);

        if (settings is null)
        {
            return Result.Fail($"{settingsPath} could not be read as JSON; fleet left it untouched.");
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
            return Result.Fail($"{userSettingsPath} could not be read as JSON; fleet left it untouched.");
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

    public Result<bool> SetTheme(string userSettingsPath, string theme)
    {
        var file = Read(
            userSettingsPath, ClaudeJsonContext.Default.UserSettingsFile, () => new UserSettingsFile());

        if (file is null)
        {
            return Result<bool>.Fail($"{userSettingsPath} could not be read as JSON; fleet left it untouched.");
        }

        if (file.Extra.TryGetValue(ThemeKey, out var current)
            && current.ValueKind == JsonValueKind.String
            && current.GetString() == theme)
        {
            return Result<bool>.Ok(false);
        }

        file.Extra[ThemeKey] = JsonSerializer.SerializeToElement(theme, ClaudeJsonContext.Default.String);

        return Write(userSettingsPath, JsonSerializer.Serialize(file, ClaudeJsonContext.Default.UserSettingsFile))
            ? Result<bool>.Ok(true)
            : Result<bool>.Fail($"fleet could not write {userSettingsPath}.");
    }

    public Result<bool> WriteTheme(string themeFilePath, ClaudeThemeFile theme)
    {
        var wanted = JsonSerializer.Serialize(theme, ClaudeJsonContext.Default.ClaudeThemeFile);

        if (File.Exists(themeFilePath) && File.ReadAllText(themeFilePath) == wanted)
        {
            return Result<bool>.Ok(false);
        }

        return Write(themeFilePath, wanted)
            ? Result<bool>.Ok(true)
            : Result<bool>.Fail($"fleet could not write {themeFilePath}.");
    }

    private const string ThemeKey = "theme";

    public Result SyncWorktree(
        McpServerEntry server,
        string directory,
        IReadOnlyList<string> allow,
        IReadOnlyList<string> deny,
        IReadOnlyList<string> ask,
        string statusHook = "")
    {
        var mcpPath = Path.Combine(directory, ".mcp.json");
        var settingsPath = Path.Combine(directory, ".claude", "settings.local.json");

        var mcp = ReadMcp(mcpPath);
        var settings = ReadSettings(settingsPath);

        if (mcp is null)
        {
            return Result.Fail($"{mcpPath} could not be read as JSON; fleet left it untouched.");
        }

        if (settings is null)
        {
            return Result.Fail($"{settingsPath} could not be read as JSON; fleet left it untouched.");
        }

        Apply(mcp, server);

        if (!settings.EnabledMcpjsonServers.Contains(server.Name))
        {
            settings.EnabledMcpjsonServers.Add(server.Name);
        }

        settings.EnableAllProjectMcpServers = true;
        settings.Permissions.Allow = Merge(settings.Permissions.Allow, allow);
        settings.Permissions.Deny = Merge(settings.Permissions.Deny, deny);
        settings.Permissions.Ask = Merge(settings.Permissions.Ask, ask);
        ApplyStatusHooks(settings, statusHook);

        if (!Write(mcpPath, JsonSerializer.Serialize(mcp, ClaudeJsonContext.Default.McpJsonFile)))
        {
            return Result.Fail($"fleet could not write {mcpPath}.");
        }

        return Write(
            settingsPath, JsonSerializer.Serialize(settings, ClaudeJsonContext.Default.ClaudeSettingsFile))
            ? Result.Ok()
            : Result.Fail($"fleet could not write {settingsPath}.");
    }

    public Result TrustFolder(string claudeJsonPath, string folder, string serverName)
    {
        var file = Read(
            claudeJsonPath, ClaudeJsonContext.Default.ClaudeGlobalFile, () => new ClaudeGlobalFile());

        if (file is null)
        {
            return Result.Fail($"{claudeJsonPath} could not be read as JSON; fleet left it untouched.");
        }

        var key = Path.GetFullPath(folder).Replace('\\', '/');

        if (!file.Projects.TryGetValue(key, out var entry))
        {
            entry = new ClaudeProjectEntry();
            file.Projects[key] = entry;
        }

        var enabled = entry.EnabledMcpjsonServers ?? [];
        var alreadyEnabled = enabled.Contains(serverName);

        if (entry.HasTrustDialogAccepted == true && alreadyEnabled)
        {
            return Result.Ok();
        }

        entry.HasTrustDialogAccepted = true;

        if (!alreadyEnabled)
        {
            enabled.Add(serverName);
            entry.EnabledMcpjsonServers = enabled;
        }

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

        var statusHooksInstalled = settings.Hooks is { } hooks
            && HookStatus.Events.All(name => hooks.For(name)?.Any(group => group.Hooks.Any(IsStatusHook)) ?? false);

        return new ClaudeState(
            mcp.McpServers.ContainsKey(serverName),
            settings.EnabledMcpjsonServers.Contains(serverName),
            hookInstalled,
            settings.Permissions.Allow,
            statusHooksInstalled);
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
        ApplyStatusHooks(file, plan.StatusHook);
    }

    private static void ApplyStatusHooks(ClaudeSettingsFile file, string command)
    {
        var on = command.Trim().Length > 0;

        if (!on && file.Hooks is null)
        {
            return;
        }

        var hooks = file.Hooks ??= new HooksJson();

        foreach (var name in HookStatus.Events)
        {
            var kept = (hooks.For(name) ?? [])
                .Where(group => !group.Hooks.Any(IsStatusHook))
                .ToList();

            if (on)
            {
                kept.Add(new HookGroup
                {
                    Hooks = [new HookEntry { Type = "command", Command = command, Args = [HookStatus.Verb], Timeout = StatusHookTimeout }],
                });
            }

            hooks.Set(name, kept.Count == 0 ? null : kept);
        }
    }

    private const double StatusHookTimeout = 10;

    private static bool IsStatusHook(HookEntry entry) =>
        entry.Args is [var verb, ..]
        && verb == HookStatus.Verb
        && IsFleetExecutable(entry.Command);

    private const string FleetExecutableName = "fleet";

    private static bool IsFleetExecutable(string command)
    {
        var path = command.Trim().Trim('"', '\'');
        var name = path[(path.LastIndexOfAny(['/', '\\']) + 1)..];

        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^".exe".Length];
        }

        return string.Equals(FleetExecutableName, name, StringComparison.OrdinalIgnoreCase);
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
            Hooks = [new HookEntry { Type = "command", Command = command, Args = [.. args], Timeout = DispatchHookTimeout }],
        });
    }

    private const double DispatchHookTimeout = 90;

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
        || rule.Equals("mcp__fleet", StringComparison.Ordinal)
        || GitGates.IsOwned(rule);

    private static McpJsonFile? ReadMcp(string path) =>
        Read(path, ClaudeJsonContext.Default.McpJsonFile, () => new McpJsonFile());

    private static ClaudeSettingsFile? ReadSettings(string path) =>
        Read(path, ClaudeJsonContext.Default.ClaudeSettingsFile, () => new ClaudeSettingsFile());

    private static T? Read<T>(
        string path, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> info, Func<T> empty)
        where T : class
    {
        var text = BusyFiles.Retry(
            () => File.Exists(path) ? File.ReadAllText(path) : string.Empty, BusyFiles.Patience);

        if (text is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return empty();
        }

        try
        {
            return JsonSerializer.Deserialize(text, info) ?? empty();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool Write(string path, string content) =>
        BusyFiles.Replace(path, temp => File.WriteAllText(temp, content));
}
