using Fleet.Platform.Claude.Models;
using Fleet.Shared.Themes;

namespace Fleet.Platform.Claude;

public static class ClaudeTheme
{
    public const string Slug = "fleet";

    public const string SettingValue = "custom:" + Slug;

    public static string BaseFor(ThemePalette theme) => theme.Light ? "light" : "dark";

    public static ClaudeThemeFile Generate(ThemePalette theme)
    {
        var magenta = theme.Ansi[5];
        var cyan = theme.Ansi[6];

        return new ClaudeThemeFile
        {
            Name = $"fleet: {theme.Title}",
            Base = BaseFor(theme),
            Overrides = new Dictionary<string, string>
            {
                ["claude"] = theme.Lavender,
                ["claudeShimmer"] = HexColor.Mix(theme.Lavender, theme.Text, 0.4),
                ["text"] = theme.Text,
                ["inverseText"] = theme.Base,
                ["inactive"] = theme.Overlay0,
                ["inactiveShimmer"] = HexColor.Mix(theme.Overlay0, theme.Text, 0.4),
                ["subtle"] = theme.Surface1,
                ["suggestion"] = theme.Blue,
                ["permission"] = theme.Blue,
                ["permissionShimmer"] = HexColor.Mix(theme.Blue, theme.Text, 0.4),
                ["remember"] = theme.Lavender,
                ["success"] = theme.Green,
                ["error"] = theme.Red,
                ["warning"] = theme.Yellow,
                ["warningShimmer"] = HexColor.Mix(theme.Yellow, theme.Text, 0.4),
                ["merged"] = magenta,
                ["promptBorder"] = theme.Surface1,
                ["promptBorderShimmer"] = theme.Overlay0,
                ["planMode"] = cyan,
                ["autoAccept"] = magenta,
                ["bashBorder"] = magenta,
                ["ide"] = theme.Blue,
                ["diffAdded"] = HexColor.Mix(theme.Base, theme.Green, 0.25),
                ["diffRemoved"] = HexColor.Mix(theme.Base, theme.Red, 0.25),
                ["diffAddedDimmed"] = HexColor.Mix(theme.Base, theme.Green, 0.12),
                ["diffRemovedDimmed"] = HexColor.Mix(theme.Base, theme.Red, 0.12),
                ["diffAddedWord"] = HexColor.Mix(theme.Base, theme.Green, 0.5),
                ["diffRemovedWord"] = HexColor.Mix(theme.Base, theme.Red, 0.5),
                ["userMessageBackground"] = theme.Surface0,
                ["userMessageBackgroundHover"] = theme.Surface1,
                ["bashMessageBackgroundColor"] = theme.Mantle,
                ["memoryBackgroundColor"] = theme.Mantle,
                ["selectionBg"] = theme.Surface1,
                ["red_FOR_SUBAGENTS_ONLY"] = theme.Red,
                ["blue_FOR_SUBAGENTS_ONLY"] = theme.Blue,
                ["green_FOR_SUBAGENTS_ONLY"] = theme.Green,
                ["yellow_FOR_SUBAGENTS_ONLY"] = theme.Yellow,
                ["purple_FOR_SUBAGENTS_ONLY"] = magenta,
                ["cyan_FOR_SUBAGENTS_ONLY"] = cyan,
            },
        };
    }
}
