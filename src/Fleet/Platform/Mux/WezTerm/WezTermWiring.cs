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
            "-- Catppuccin visuals, pill bars and tmux-parity keys (prefix ctrl+s).",
            "-- Generated as fleet-theme.lua; drop this block to bring your own look.",
            "local ok_theme, theme = pcall(require, 'fleet-theme')",
            "if ok_theme then",
            "  theme.apply(config)",
            "end",
            string.Empty,
            "-- fleet: prefix chord, menu and status helpers.",
            "local ok_fleet, fleet = pcall(require, 'fleet')",
            "if ok_fleet then",
            $"  {ApplyLine}",
            "end",
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

    public static string Unwire(string text)
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

        return text.Contains(block, StringComparison.Ordinal)
            ? text.Replace(block, string.Empty, StringComparison.Ordinal)
            : text;
    }

    private static bool Returns(string line)
    {
        var trimmed = line.Trim();

        return trimmed.StartsWith("return config", StringComparison.Ordinal);
    }
}
