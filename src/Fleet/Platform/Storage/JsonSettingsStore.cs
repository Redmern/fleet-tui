using System.Text.Json;
using Fleet.Platform.Storage.Models;
using Fleet.Ports.Settings;
using Fleet.Shared;
using Fleet.Shared.Aidlc;
using Fleet.Shared.Aidlc.Enums;
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
                    ParsePolicy(stored.Push, SettingsDefaults.Push),
                    ParseAidlc(stored),
                    ParseOnOff(stored.MainOrchestratorInNvim, SettingsDefaults.MainOrchestratorInNvim),
                    ParseOnOff(stored.SubOrchestratorsInNvim, SettingsDefaults.SubOrchestratorsInNvim),
                    ParseOnOff(stored.AutoClose, SettingsDefaults.AutoClose),
                    stored.AutoCloseMinutes,
                    ParsePolicy(stored.Merge, SettingsDefaults.Merge))
                .MergedOverDefaults() with
            {
                StatusHooks = stored.StatusHooks ?? SettingsDefaults.StatusHooks,
            };
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
            Merge = PolicyAgainstDefault(config.Merge, SettingsDefaults.Merge),
            Aidlc = WordAgainstDefault(config.Aidlc.Mode, SettingsDefaults.Aidlc.Mode),
            AidlcProfile = WordAgainstDefault(config.Aidlc.DefaultProfile, SettingsDefaults.Aidlc.DefaultProfile),
            AidlcAutonomy = WordAgainstDefault(config.Aidlc.Autonomy, SettingsDefaults.Aidlc.Autonomy),
            AidlcOff = [.. AidlcSettings.Parts.Where(p => !config.Aidlc.IsOn(p)).Select(Words.Of)],
            MainOrchestratorInNvim = OnOffAgainstDefault(
                config.MainOrchestratorInNvim, SettingsDefaults.MainOrchestratorInNvim),
            SubOrchestratorsInNvim = OnOffAgainstDefault(
                config.SubOrchestratorsInNvim, SettingsDefaults.SubOrchestratorsInNvim),
            AutoClose = OnOffAgainstDefault(config.AutoClose, SettingsDefaults.AutoClose),
            AutoCloseMinutes = config.AutoCloseMinutes == SettingsDefaults.AutoCloseMinutes ? 0 : config.AutoCloseMinutes,
            StatusHooks = config.StatusHooks == SettingsDefaults.StatusHooks ? null : config.StatusHooks,
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

    private static AidlcSettings ParseAidlc(SettingsFile stored)
    {
        var fallback = SettingsDefaults.Aidlc;

        var off = stored.AidlcOff
            .Select(Words.Parse<AidlcPart>)
            .Where(p => p is { } part && AidlcSettings.Parts.Contains(part))
            .Aggregate(AidlcPart.None, (all, p) => all | p!.Value);

        return new AidlcSettings(
            Words.Parse<AidlcMode>(stored.Aidlc) ?? fallback.Mode,
            Words.Parse<Profile>(stored.AidlcProfile) ?? fallback.DefaultProfile,
            Words.Parse<Autonomy>(stored.AidlcAutonomy) ?? fallback.Autonomy,
            off);
    }

    private static string WordAgainstDefault<T>(T value, T fallback)
        where T : struct, Enum =>
        EqualityComparer<T>.Default.Equals(value, fallback) ? string.Empty : Words.Of(value);

    private static bool ParseOnOff(string stored, bool fallback) => stored.Trim().ToLowerInvariant() switch
    {
        "on" => true,
        "off" => false,
        _ => fallback,
    };

    private static string OnOffAgainstDefault(bool value, bool fallback) =>
        value == fallback ? string.Empty : value ? "on" : "off";

    private static string? FileFor(string project)
    {
        var name = ProjectName.Sanitize(project);

        return name.Length == 0 ? null : Path.Combine(FleetPaths.Settings, name + ".json");
    }
}
