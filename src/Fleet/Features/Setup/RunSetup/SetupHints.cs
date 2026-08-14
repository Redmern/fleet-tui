namespace Fleet.Features.Setup.RunSetup;

public static class SetupHints
{
    public static string For(string tool) => OperatingSystem.IsWindows()
        ? Windows(tool)
        : Linux(tool);

    public const string Glyphs =
        "if the pills above look like boxes, point wezterm at a Nerd Font";

    private static string Windows(string tool) => tool switch
    {
        "wezterm" => "winget install wez.wezterm",
        "git" => "winget install Git.Git",
        "nvim" => "winget install Neovim.Neovim",
        "claude" => "npm install -g @anthropic-ai/claude-code",
        "yazi" => "winget install sxyazi.yazi",
        _ => $"install {tool} and put it on PATH",
    };

    private static string Linux(string tool) => tool switch
    {
        "claude" => "npm install -g @anthropic-ai/claude-code",
        "nvim" => "install neovim with your package manager",
        "yazi" => "install yazi with your package manager, or see https://yazi-rs.github.io",
        _ => $"install {tool} with your package manager",
    };
}
