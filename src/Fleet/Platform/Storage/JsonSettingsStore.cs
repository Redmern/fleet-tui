using System.Text.Json;
using Fleet.Platform.Storage.Models;
using Fleet.Ports.Settings;
using Fleet.Shared;
using Fleet.Shared.Settings;
using Fleet.Shared.Settings.Enums;
using Fleet.Shared.Settings.Models;

namespace Fleet.Platform.Storage;

public sealed class JsonSettingsStore : ISettingsStore
{
    public SettingsConfig Load(string project)
    {
        var file = FileFor(project);

        if (file is null || !File.Exists(file))
        {
            return SettingsConfig.Default;
        }

        try
        {
            var stored = JsonSerializer.Deserialize(
                File.ReadAllText(file), FleetJsonContext.Default.SettingsFile);

            if (stored is null)
            {
                return SettingsConfig.Default;
            }

            var rules = new Dictionary<HarnessTool, ToolRule>();

            foreach (var (id, entry) in stored.Tools)
            {
                var tool = HarnessToolIds.Parse(id);

                if (tool == HarnessTool.None
                    || !Enum.TryParse<ActionPolicy>(entry.Policy, ignoreCase: true, out var policy))
                {
                    continue;
                }

                var channel = Enum.TryParse<AskChannel>(
                    entry.Channel, ignoreCase: true, out var parsed)
                    ? parsed
                    : AskChannel.Both;

                rules[tool] = new ToolRule(policy, channel);
            }

            return new SettingsConfig(
                    stored.Trigger,
                    rules,
                    ParsePolicy(stored.Commit, SettingsDefaults.Commit),
                    ParsePolicy(stored.Push, SettingsDefaults.Push))
                .MergedOverDefaults();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return SettingsConfig.Default;
        }
    }

    public void Save(string project, SettingsConfig config)
    {
        var file = FileFor(project);

        if (file is null)
        {
            return;
        }

        var stored = new SettingsFile
        {
            Trigger = SettingsDiff.TriggerAgainstDefault(config.Trigger),
            Commit = PolicyAgainstDefault(config.Commit, SettingsDefaults.Commit),
            Push = PolicyAgainstDefault(config.Push, SettingsDefaults.Push),
            Tools = SettingsDiff.AgainstDefaults(config.Rules).ToDictionary(
                r => HarnessToolIds.For(r.Key),
                r => new ToolRuleEntry
                {
                    Policy = r.Value.Policy.ToString().ToLowerInvariant(),
                    Channel = r.Value.Channel.ToString().ToLowerInvariant(),
                }),
        };

        try
        {
            FleetPaths.EnsureDirs();
            File.WriteAllText(
                file, JsonSerializer.Serialize(stored, FleetJsonContext.Default.SettingsFile));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static ActionPolicy ParsePolicy(string stored, ActionPolicy fallback) =>
        Enum.TryParse<ActionPolicy>(stored, ignoreCase: true, out var parsed) ? parsed : fallback;

    private static string PolicyAgainstDefault(ActionPolicy value, ActionPolicy fallback) =>
        value == fallback ? string.Empty : value.ToString().ToLowerInvariant();

    private static string? FileFor(string project)
    {
        var name = ProjectName.Sanitize(project);

        return name.Length == 0 ? null : Path.Combine(FleetPaths.Settings, name + ".json");
    }
}
