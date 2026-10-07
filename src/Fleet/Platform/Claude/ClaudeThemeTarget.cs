using Fleet.Ports.Themes;
using Fleet.Ports.Themes.Models;
using Fleet.Shared.Themes;

namespace Fleet.Platform.Claude;

public sealed class ClaudeThemeTarget(string configDirectory) : IThemeTarget
{
    public string Tool => "claude";

    public string SettingsFile => Path.Combine(configDirectory, "settings.json");

    public string ThemeFile => Path.Combine(configDirectory, "themes", ClaudeTheme.Slug + ".json");

    public ThemeApplied Apply(ThemePalette theme)
    {
        if (!Directory.Exists(configDirectory))
        {
            return ThemeApplied.Skipped(Tool, $"no Claude Code config folder at {configDirectory}");
        }

        var writer = new ClaudeConfigWriter();
        var file = writer.WriteTheme(ThemeFile, ClaudeTheme.Generate(theme));

        if (!file.Succeeded)
        {
            return ThemeApplied.Failed(Tool, file.Error!);
        }

        var setting = writer.SetTheme(SettingsFile, ClaudeTheme.SettingValue);

        if (!setting.Succeeded)
        {
            return ThemeApplied.Failed(Tool, setting.Error!);
        }

        var summary = $"\"{ClaudeTheme.SettingValue}\" on a {ClaudeTheme.BaseFor(theme)} base";

        return file.Value || setting.Value
            ? ThemeApplied.Applied(Tool, $"wrote {ThemeFile} and set {summary} in {SettingsFile}")
            : ThemeApplied.Unchanged(Tool, $"{summary} already in {SettingsFile}");
    }
}
