namespace Fleet.Shared.Themes;

public static class BuiltInThemes
{
    public const string DefaultName = "catppuccin-mocha";

    public static ThemePalette CatppuccinMocha { get; } = Make(
        DefaultName, "Catppuccin Mocha", false,
        "#11111b #181825 #1e1e2e #313244 #45475a #6c7086 #a6adc8 #cdd6f4",
        "#89b4fa #b4befe #a6e3a1 #f9e2af #f38ba8 #f5e0dc",
        "#45475a #f38ba8 #a6e3a1 #f9e2af #89b4fa #f5c2e7 #94e2d5 #bac2de",
        "#585b70 #f38ba8 #a6e3a1 #f9e2af #89b4fa #f5c2e7 #94e2d5 #a6adc8");

    public static IReadOnlyList<ThemePalette> All { get; } =
    [
        CatppuccinMocha,
        Make(
            "catppuccin-latte", "Catppuccin Latte", true,
            "#dce0e8 #e6e9ef #eff1f5 #ccd0da #bcc0cc #9ca0b0 #6c6f85 #4c4f69",
            "#1e66f5 #7287fd #40a02b #df8e1d #d20f39 #dc8a78",
            "#5c5f77 #d20f39 #40a02b #df8e1d #1e66f5 #ea76cb #179299 #acb0be",
            "#6c6f85 #d20f39 #40a02b #df8e1d #1e66f5 #ea76cb #179299 #bcc0cc"),
        Make(
            "tokyo-night", "Tokyo Night", false,
            "#15161e #16161e #1a1b26 #292e42 #3b4261 #565f89 #a9b1d6 #c0caf5",
            "#7aa2f7 #bb9af7 #9ece6a #e0af68 #f7768e #c0caf5",
            "#15161e #f7768e #9ece6a #e0af68 #7aa2f7 #bb9af7 #7dcfff #a9b1d6",
            "#414868 #f7768e #9ece6a #e0af68 #7aa2f7 #bb9af7 #7dcfff #c0caf5"),
        Make(
            "gruvbox-dark", "Gruvbox Dark", false,
            "#1d2021 #1d2021 #282828 #3c3836 #504945 #928374 #bdae93 #ebdbb2",
            "#83a598 #d3869b #b8bb26 #fabd2f #fb4934 #ebdbb2",
            "#282828 #cc241d #98971a #d79921 #458588 #b16286 #689d6a #a89984",
            "#928374 #fb4934 #b8bb26 #fabd2f #83a598 #d3869b #8ec07c #ebdbb2"),
        Make(
            "gruvbox-light", "Gruvbox Light", true,
            "#f2e5bc #f2e5bc #fbf1c7 #ebdbb2 #d5c4a1 #928374 #665c54 #3c3836",
            "#076678 #8f3f71 #79740e #b57614 #9d0006 #3c3836",
            "#fbf1c7 #cc241d #98971a #d79921 #458588 #b16286 #689d6a #7c6f64",
            "#928374 #9d0006 #79740e #b57614 #076678 #8f3f71 #427b58 #3c3836"),
        Make(
            "nord", "Nord", false,
            "#242933 #292e39 #2e3440 #3b4252 #434c5e #616e88 #d8dee9 #eceff4",
            "#81a1c1 #88c0d0 #a3be8c #ebcb8b #bf616a #d8dee9",
            "#3b4252 #bf616a #a3be8c #ebcb8b #81a1c1 #b48ead #88c0d0 #e5e9f0",
            "#4c566a #bf616a #a3be8c #ebcb8b #81a1c1 #b48ead #8fbcbb #eceff4"),
        Make(
            "dracula", "Dracula", false,
            "#191a21 #21222c #282a36 #343746 #44475a #6272a4 #bfbfbf #f8f8f2",
            "#bd93f9 #ff79c6 #50fa7b #f1fa8c #ff5555 #f8f8f2",
            "#21222c #ff5555 #50fa7b #f1fa8c #bd93f9 #ff79c6 #8be9fd #f8f8f2",
            "#6272a4 #ff6e6e #69ff94 #ffffa5 #d6acff #ff92df #a4ffff #ffffff"),
        Make(
            "solarized-dark", "Solarized Dark", false,
            "#00212b #002631 #002b36 #073642 #174652 #586e75 #839496 #93a1a1",
            "#268bd2 #6c71c4 #859900 #b58900 #dc322f #93a1a1",
            "#073642 #dc322f #859900 #b58900 #268bd2 #d33682 #2aa198 #eee8d5",
            "#002b36 #cb4b16 #586e75 #657b83 #839496 #6c71c4 #93a1a1 #fdf6e3"),
        Make(
            "solarized-light", "Solarized Light", true,
            "#e9e2cb #f4eedb #fdf6e3 #eee8d5 #ddd6c1 #93a1a1 #657b83 #586e75",
            "#268bd2 #6c71c4 #859900 #b58900 #dc322f #586e75",
            "#073642 #dc322f #859900 #b58900 #268bd2 #d33682 #2aa198 #eee8d5",
            "#002b36 #cb4b16 #586e75 #657b83 #839496 #6c71c4 #93a1a1 #fdf6e3"),
        Make(
            "rose-pine", "Rosé Pine", false,
            "#13111e #16141f #191724 #26233a #403d52 #6e6a86 #908caa #e0def4",
            "#c4a7e7 #ebbcba #9ccfd8 #f6c177 #eb6f92 #e0def4",
            "#26233a #eb6f92 #31748f #f6c177 #9ccfd8 #c4a7e7 #ebbcba #e0def4",
            "#6e6a86 #eb6f92 #31748f #f6c177 #9ccfd8 #c4a7e7 #ebbcba #e0def4"),
        Make(
            "everforest", "Everforest", false,
            "#1e2326 #232a2e #2d353b #343f44 #475258 #7a8478 #9da9a0 #d3c6aa",
            "#7fbbb3 #d699b6 #a7c080 #dbbc7f #e67e80 #d3c6aa",
            "#343f44 #e67e80 #a7c080 #dbbc7f #7fbbb3 #d699b6 #83c092 #d3c6aa",
            "#7a8478 #e67e80 #a7c080 #dbbc7f #7fbbb3 #d699b6 #83c092 #d3c6aa"),
        Make(
            "kanagawa", "Kanagawa", false,
            "#16161d #181820 #1f1f28 #2a2a37 #363646 #727169 #c8c093 #dcd7ba",
            "#7e9cd8 #957fb8 #98bb6c #e6c384 #e46876 #c8c093",
            "#090618 #c34043 #76946a #c0a36e #7e9cd8 #957fb8 #6a9589 #c8c093",
            "#727169 #e82424 #98bb6c #e6c384 #7fb4ca #938aa9 #7aa89f #dcd7ba"),
        Make(
            "one-dark", "One Dark", false,
            "#1e2227 #21252b #282c34 #2c313a #3e4451 #5c6370 #828997 #abb2bf",
            "#61afef #c678dd #98c379 #e5c07b #e06c75 #528bff",
            "#282c34 #e06c75 #98c379 #e5c07b #61afef #c678dd #56b6c2 #abb2bf",
            "#5c6370 #e06c75 #98c379 #e5c07b #61afef #c678dd #56b6c2 #c8ccd4"),
        Make(
            "ayu-dark", "Ayu Dark", false,
            "#070a0f #0d1017 #0b0e14 #131721 #273747 #565b66 #8a9199 #bfbdb6",
            "#59c2ff #e6b450 #aad94c #ffb454 #f07178 #e6b450",
            "#01060e #ea6c73 #7fd962 #f9af4f #53bdfa #cda1fa #90e1c6 #c7c7c7",
            "#686868 #f07178 #aad94c #ffb454 #59c2ff #d2a6ff #95e6cb #ffffff"),
        Make(
            "github-dark", "GitHub Dark", false,
            "#010409 #090c10 #0d1117 #161b22 #30363d #6e7681 #8b949e #e6edf3",
            "#58a6ff #d2a8ff #3fb950 #d29922 #f85149 #2f81f7",
            "#484f58 #ff7b72 #3fb950 #d29922 #58a6ff #bc8cff #39c5cf #b1bac4",
            "#6e7681 #ffa198 #56d364 #e3b341 #79c0ff #d2a8ff #56d4dd #ffffff"),
    ];

    public static ThemePalette? Find(string name)
    {
        var key = ThemeNames.Normalize(name);

        return All.FirstOrDefault(t => t.Name == key || ThemeNames.Normalize(t.Title) == key);
    }

    private static ThemePalette Make(
        string name, string title, bool light, string surfaces, string accents, string ansi, string brights)
    {
        var s = Colors(surfaces, 8);
        var a = Colors(accents, 6);

        return new ThemePalette(
            name, title, light,
            s[0], s[1], s[2], s[3], s[4], s[5], s[6], s[7],
            a[0], a[1], a[2], a[3], a[4], a[5],
            Colors(ansi, ThemePalette.AnsiCount),
            Colors(brights, ThemePalette.AnsiCount));
    }

    private static string[] Colors(string list, int count)
    {
        var colors = list.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return colors.Length == count
            ? colors
            : throw new ArgumentException($"expected {count} colors, got {colors.Length}", nameof(list));
    }
}
