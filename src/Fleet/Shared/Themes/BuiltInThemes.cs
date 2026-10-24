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
        Make(
            "catppuccin-macchiato", "Catppuccin Macchiato", false,
            "#181926 #1e2030 #24273a #363a4f #494d64 #6e738d #a5adcb #cad3f5",
            "#8aadf4 #b7bdf8 #a6da95 #eed49f #ed8796 #f4dbd6",
            "#494d64 #ed8796 #a6da95 #eed49f #8aadf4 #f5bde6 #8bd5ca #b8c0e0",
            "#5b6078 #ed8796 #a6da95 #eed49f #8aadf4 #f5bde6 #8bd5ca #a5adcb"),
        Make(
            "tokyo-night-storm", "Tokyo Night Storm", false,
            "#1b1e2d #1f2335 #24283b #292e42 #3b4261 #565f89 #a9b1d6 #c0caf5",
            "#7aa2f7 #bb9af7 #9ece6a #e0af68 #f7768e #c0caf5",
            "#1d202f #f7768e #9ece6a #e0af68 #7aa2f7 #bb9af7 #7dcfff #a9b1d6",
            "#414868 #f7768e #9ece6a #e0af68 #7aa2f7 #bb9af7 #7dcfff #c0caf5"),
        Make(
            "monokai", "Monokai", false,
            "#1e1f1c #23241f #272822 #3e3d32 #49483e #75715e #cfcfc2 #f8f8f2",
            "#66d9ef #ae81ff #a6e22e #e6db74 #f92672 #f8f8f0",
            "#272822 #f92672 #a6e22e #f4bf75 #66d9ef #ae81ff #a1efe4 #f8f8f2",
            "#75715e #f92672 #a6e22e #f4bf75 #66d9ef #ae81ff #a1efe4 #f9f8f5"),
        Make(
            "material-ocean", "Material Ocean", false,
            "#090b10 #0c0e15 #0f111a #1f2233 #3b3f51 #464b5d #717cb4 #a6accd",
            "#82aaff #c792ea #c3e88d #ffcb6b #f07178 #ffcc00",
            "#546e7a #ff5370 #c3e88d #ffcb6b #82aaff #c792ea #89ddff #ffffff",
            "#546e7a #ff5370 #c3e88d #ffcb6b #82aaff #c792ea #89ddff #ffffff"),
        Make(
            "palenight", "Palenight", false,
            "#1b1e2b #202331 #292d3e #34324a #444267 #676e95 #8796b0 #a6accd",
            "#82aaff #c792ea #c3e88d #ffcb6b #f07178 #ffcc00",
            "#292d3e #f07178 #c3e88d #ffcb6b #82aaff #c792ea #89ddff #d0d0d0",
            "#434758 #ff8b92 #ddffa7 #ffe585 #9cc4ff #e1acff #a3f7ff #ffffff"),
        Make(
            "night-owl", "Night Owl", false,
            "#010e1a #01111d #011627 #0b2942 #1d3b53 #637777 #89a4bb #d6deeb",
            "#82aaff #c792ea #addb67 #ecc48d #ef5350 #80a4c2",
            "#011627 #ef5350 #22da6e #addb67 #82aaff #c792ea #21c7a8 #ffffff",
            "#575656 #ef5350 #22da6e #ffeb95 #82aaff #c792ea #7fdbca #ffffff"),
        Make(
            "github-light", "GitHub Light", true,
            "#eaeef2 #f6f8fa #ffffff #eaeef2 #d0d7de #8c959f #57606a #24292f",
            "#0969da #8250df #1a7f37 #9a6700 #cf222e #0969da",
            "#24292f #cf222e #116329 #4d2d00 #0969da #8250df #1b7c83 #6e7781",
            "#57606a #a40e26 #1a7f37 #633c01 #218bff #a475f9 #3192aa #8c959f"),
        Make(
            "ayu-light", "Ayu Light", true,
            "#e7eaed #f3f4f5 #fcfcfc #e6eaef #d8dce3 #adaeb1 #8a9199 #5c6166",
            "#399ee6 #a37acc #86b300 #f2ae49 #f07171 #ff9940",
            "#000000 #ea6c6d #6cbf43 #eca944 #3199e1 #9e75c7 #46ba94 #bababa",
            "#686868 #f07171 #86b300 #f2ae49 #399ee6 #a37acc #4cbf99 #d1d1d1"),
        Make(
            "one-light", "One Light", true,
            "#e5e5e6 #f0f0f1 #fafafa #eaeaeb #dbdbdc #a0a1a7 #696c77 #383a42",
            "#4078f2 #a626a4 #50a14f #c18401 #e45649 #526fff",
            "#383a42 #e45649 #50a14f #c18401 #0184bc #a626a4 #0997b3 #fafafa",
            "#4f525e #e06c75 #98c379 #e5c07b #61afef #c678dd #56b6c2 #ffffff"),
        Make(
            "rose-pine-dawn", "Rosé Pine Dawn", true,
            "#f2e9e1 #fffaf3 #faf4ed #f4ede8 #dfdad9 #9893a5 #797593 #575279",
            "#286983 #907aa9 #56949f #ea9d34 #b4637a #575279",
            "#f2e9e1 #b4637a #286983 #ea9d34 #56949f #907aa9 #d7827e #575279",
            "#9893a5 #b4637a #286983 #ea9d34 #56949f #907aa9 #d7827e #575279"),
        Make(
            "tokyo-night-day", "Tokyo Night Day", true,
            "#c4c8da #d0d5e3 #e1e2e7 #d5d6db #a8aecb #848cb5 #6172b0 #3760bf",
            "#2e7de9 #9854f1 #587539 #8c6c3e #f52a65 #3760bf",
            "#e9e9ed #f52a65 #587539 #8c6c3e #2e7de9 #9854f1 #007197 #6172b0",
            "#a1a6c5 #f52a65 #587539 #8c6c3e #2e7de9 #9854f1 #007197 #3760bf"),
        Make(
            "everforest-light", "Everforest Light", true,
            "#efebd4 #f4f0d9 #fdf6e3 #e6e2cc #e0dcc7 #a6b0a0 #829181 #5c6a72",
            "#3a94c5 #df69ba #8da101 #dfa000 #f85552 #5c6a72",
            "#5c6a72 #f85552 #8da101 #dfa000 #3a94c5 #df69ba #35a77c #e0dcc7",
            "#829181 #f85552 #8da101 #dfa000 #3a94c5 #df69ba #35a77c #fdf6e3"),
        Make(
            "kanagawa-lotus", "Kanagawa Lotus", true,
            "#d5cea3 #e5ddb0 #f2ecbc #e7dba0 #e4d794 #8a8980 #716e61 #545464",
            "#4d699b #624c83 #6f894e #77713f #c84053 #43436c",
            "#1f1f28 #c84053 #6f894e #77713f #4d699b #b35b79 #597b75 #545464",
            "#8a8980 #d7474b #6e915f #836f4a #6693bf #624c83 #5e857a #43436c"),
        Make(
            "material-lighter", "Material Lighter", true,
            "#e7eaec #eeeeee #fafafa #e7e7e8 #d2d4d5 #90a4ae #7e939e #546e7a",
            "#6182b8 #7c4dff #91b859 #f6a434 #e53935 #272727",
            "#000000 #e53935 #91b859 #f6a434 #6182b8 #7c4dff #39adb5 #a0a0a0",
            "#546e7a #ff5370 #91b859 #ffb62c #6182b8 #945eb8 #39adb5 #e7eaec"),
        Make(
            "night-owl-light", "Night Owl Light", true,
            "#e0e7ea #f0f0f0 #fbfbfb #e0e0e0 #d9d9d9 #989fb1 #7a8181 #403f53",
            "#4876d6 #994cc3 #08916a #daaa01 #de3d3b #90a7b2",
            "#011627 #d3423e #2aa298 #daaa01 #4876d6 #403f53 #08916a #7a8181",
            "#7a8181 #f76e6e #49d0c5 #dac26b #5ca7e4 #697098 #00c990 #989fb1"),
        Make(
            "papercolor-light", "PaperColor Light", true,
            "#dadada #e4e4e4 #eeeeee #d0d0d0 #bcbcbc #878787 #5f5f5f #444444",
            "#0087af #8700af #008700 #d75f00 #af0000 #005f87",
            "#eeeeee #af0000 #008700 #5f8700 #0087af #878787 #005f87 #444444",
            "#bcbcbc #d70000 #d70087 #8700af #d75f00 #d75f00 #005faf #005f87"),
        Make(
            "modus-operandi", "Modus Operandi", true,
            "#e0e0e0 #f2f2f2 #ffffff #e0e0e0 #c4c4c4 #9f9f9f #595959 #000000",
            "#0031a9 #531ab6 #006800 #6f5500 #a60000 #000000",
            "#000000 #a60000 #006800 #6f5500 #0031a9 #721045 #005e8b #c4c4c4",
            "#595959 #972500 #316500 #884900 #3548cf #8f0075 #3f578f #ffffff"),
        Make(
            "flexoki-light", "Flexoki Light", true,
            "#e6e4d9 #f2f0e5 #fffcf0 #e6e4d9 #dad8ce #b7b5ac #6f6e69 #100f0f",
            "#205ea6 #5e409d #66800b #ad8301 #af3029 #100f0f",
            "#100f0f #af3029 #66800b #ad8301 #205ea6 #a02f6f #24837b #878580",
            "#6f6e69 #d14d41 #879a39 #d0a215 #4385be #ce5d97 #3aa99f #cecdc3"),
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
