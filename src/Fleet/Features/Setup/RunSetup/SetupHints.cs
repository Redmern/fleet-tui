using Fleet.Shared.Settings;

namespace Fleet.Features.Setup.RunSetup;

public static class SetupHints
{
    public static string For(string tool) => OperatingSystem.IsWindows()
        ? Windows(tool)
        : Linux(tool);

    public const string Glyphs =
        "if the pills above look like boxes, point your terminal at a Nerd Font";

    public static string Windows(string tool) => tool switch
    {
        "git" => "winget install Git.Git",
        "nvim" => "winget install Neovim.Neovim",
        "claude" => "npm install -g @anthropic-ai/claude-code",
        "yazi" => "winget install sxyazi.yazi",
        EditorChoice.VscodeCommand => "winget install Microsoft.VisualStudioCode",
        _ => $"install {tool} and put it on PATH",
    };

    public static string Linux(string tool) => tool switch
    {
        "claude" => "npm install -g @anthropic-ai/claude-code",
        "nvim" => "install neovim with your package manager",
        "yazi" => "install yazi with your package manager, or see https://yazi-rs.github.io",
        EditorChoice.VscodeCommand => $"install VS Code and its '{EditorChoice.VscodeCommand}' shell command",
        _ => $"install {tool} with your package manager",
    };
}
