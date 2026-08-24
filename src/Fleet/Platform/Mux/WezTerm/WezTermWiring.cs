namespace Fleet.Platform.Mux.WezTerm;

public static class WezTermWiring
{
    public const string Module = "fleet.lua";

    public const string RequireLine = "local fleet = require 'fleet'";

    public const string ApplyLine = "fleet.apply(config)";

    public static string ModuleDirectory(string home) =>
        OperatingSystem.IsWindows()
            ? Path.Combine(home, ".wezterm")
            : Path.Combine(home, ".config", "wezterm");

    public static IReadOnlyList<string> ConfigCandidates(string home) =>
    [
        Path.Combine(home, ".wezterm.lua"),
        Path.Combine(home, ".config", "wezterm", "wezterm.lua"),
    ];

    public static string DefaultConfig(string home) =>
        OperatingSystem.IsWindows()
            ? Path.Combine(home, ".wezterm.lua")
            : Path.Combine(home, ".config", "wezterm", "wezterm.lua");

    public static string Starter() =>
        string.Join(
            "\n",
            "-- created by: fleet setup",
            "local wezterm = require 'wezterm'",
            "local config = wezterm.config_builder()",
            string.Empty,
            "-- Leader for pane commands; fleet guards LEADER x so the dashboard",
            "-- pane refuses to close while any other pane closes with a confirm.",
            "config.leader = { key = 's', mods = 'CTRL', timeout_milliseconds = 2000 }",
            string.Empty,
            "-- fleet's pills and branch glyphs need a Nerd Font; the first",
            "-- installed family wins, wezterm's built-in symbols fill the rest.",
            "config.font = wezterm.font_with_fallback {",
            "  'CaskaydiaMono Nerd Font',",
            "  'JetBrainsMono Nerd Font',",
            "  'FiraCode Nerd Font',",
            "  'JetBrains Mono',",
            "}",
            string.Empty,
            "-- fleet: prefix chord, menu and status helpers.",
            "local ok_fleet, fleet = pcall(require, 'fleet')",
            "if ok_fleet then",
            $"  {ApplyLine}",
            "end",
            string.Empty,
            "-- the fleet project (or the workspace name) on the left of the tab bar.",
            "wezterm.on('update-status', function(window, _pane)",
            "  local name = (ok_fleet and fleet.label(window)) or window:active_workspace()",
            "  window:set_left_status(wezterm.format {",
            "    { Attribute = { Intensity = 'Bold' } },",
            "    { Text = ' ' .. name .. ' ' },",
            "  })",
            "end)",
            string.Empty,
            "return config",
            string.Empty);

    public static string[] Lines(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

    public static bool AlreadyWired(string text) =>
        Lines(text)
            .Where(l => !l.TrimStart().StartsWith("--", StringComparison.Ordinal))
            .Any(l => l.Contains("require", StringComparison.Ordinal)
                && l.Contains("fleet", StringComparison.OrdinalIgnoreCase));

    public static (string Text, bool BeforeReturn) Wire(string text)
    {
        var block = string.Join(
            Environment.NewLine,
            string.Empty,
            "-- fleet: prefix chord, menu and status helpers.",
            "local ok_fleet, fleet = pcall(require, 'fleet')",
            "if ok_fleet then",
            $"  {ApplyLine}",
            "end",
            string.Empty);

        var lines = Lines(text);

        for (var i = lines.Length - 1; i >= 0; i--)
        {
            if (!Returns(lines[i]))
            {
                continue;
            }

            var head = string.Join(Environment.NewLine, lines.Take(i));
            var tail = string.Join(Environment.NewLine, lines.Skip(i));

            return (head + Environment.NewLine + block + tail, true);
        }

        return (text.TrimEnd() + Environment.NewLine + block, false);
    }

    private static bool Returns(string line)
    {
        var trimmed = line.Trim();

        return trimmed.StartsWith("return config", StringComparison.Ordinal);
    }
}
