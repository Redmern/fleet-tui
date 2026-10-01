using System.Text;
using System.Text.Json;

namespace Fleet.Platform.Mux.Embedded.Host;

public static class DefaultShell
{
    public const string Variable = "FLEET_SHELL";

    public static IReadOnlyList<string> Resolve(
        Func<string, string?> variable,
        Func<string, bool> onPath,
        Func<string?> windowsTerminalSettings,
        bool windows)
    {
        if (variable(Variable) is { Length: > 0 } chosen && Split(chosen) is { Count: > 0 } own)
        {
            return own;
        }

        if (windows)
        {
            if (DefaultProfileCommand(windowsTerminalSettings()) is { } profile && Split(Environment.ExpandEnvironmentVariables(profile)) is { Count: > 0 } wt)
            {
                return wt;
            }

            if (onPath("pwsh"))
            {
                return ["pwsh.exe", "-NoLogo"];
            }

            return [variable("COMSPEC") ?? "cmd.exe"];
        }

        return [variable("SHELL") ?? "/bin/sh"];
    }

    public static string? DefaultProfileCommand(string? settingsJson)
    {
        if (string.IsNullOrWhiteSpace(settingsJson))
        {
            return null;
        }

        try
        {
            using var settings = JsonDocument.Parse(
                settingsJson, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var root = settings.RootElement;

            if (!root.TryGetProperty("defaultProfile", out var wanted) || wanted.GetString() is not { } id
                || !root.TryGetProperty("profiles", out var profiles))
            {
                return null;
            }

            var list = profiles.ValueKind == JsonValueKind.Array
                ? profiles
                : profiles.TryGetProperty("list", out var inner) ? inner : default;

            if (list.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var profile in list.EnumerateArray())
            {
                var guid = profile.TryGetProperty("guid", out var g) ? g.GetString() : null;
                var name = profile.TryGetProperty("name", out var n) ? n.GetString() : null;

                if (string.Equals(guid, id, StringComparison.OrdinalIgnoreCase) || string.Equals(name, id, StringComparison.OrdinalIgnoreCase))
                {
                    return profile.TryGetProperty("commandline", out var command) ? command.GetString() : null;
                }
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static IReadOnlyList<string> Split(string commandLine)
    {
        var parts = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        var any = false;

        foreach (var c in commandLine)
        {
            if (c == '"')
            {
                quoted = !quoted;
                any = true;
            }
            else if (char.IsWhiteSpace(c) && !quoted)
            {
                if (any)
                {
                    parts.Add(current.ToString());
                    current.Clear();
                    any = false;
                }
            }
            else
            {
                current.Append(c);
                any = true;
            }
        }

        if (any)
        {
            parts.Add(current.ToString());
        }

        return parts;
    }

    public static string? WindowsTerminalSettings()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string[] candidates =
        [
            Path.Combine(local, "Packages", "Microsoft.WindowsTerminal_8wekyb3d8bbwe", "LocalState", "settings.json"),
            Path.Combine(local, "Packages", "Microsoft.WindowsTerminalPreview_8wekyb3d8bbwe", "LocalState", "settings.json"),
            Path.Combine(local, "Microsoft", "Windows Terminal", "settings.json"),
        ];

        foreach (var file in candidates)
        {
            try
            {
                if (File.Exists(file))
                {
                    return File.ReadAllText(file);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }

        return null;
    }
}
