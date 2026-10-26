using Fleet.Shared.Aidlc;
using Fleet.Shared.Settings.Enums;

namespace Fleet.Shared.Settings;

public static class EditorChoice
{
    public const string VscodeCommand = "code";

    public const string NvimCommand = "nvim";

    public static Editor Resolve(Editor? project, Editor? global, bool nvimInstalled, bool codeInstalled) =>
        project ?? global ?? Installed(nvimInstalled, codeInstalled) ?? Editor.Nvim;

    public static Editor? Installed(bool nvimInstalled, bool codeInstalled) =>
        nvimInstalled ? Editor.Nvim : codeInstalled ? Editor.Vscode : null;

    public static bool SkipsNvimSetup(Editor editor, bool nvimInstalled) =>
        editor == Editor.Vscode && !nvimInstalled;

    public static Editor? Parse(string? stored) => Words.Parse<Editor>(stored);

    public static string Word(Editor? editor) => editor is { } chosen ? Words.Of(chosen) : string.Empty;

    public static string InstallHint(bool windows) =>
        windows
            ? $"'{VscodeCommand}' is not on PATH. Install VS Code with 'winget install Microsoft.VisualStudioCode', then restart fleet."
            : $"'{VscodeCommand}' is not on PATH. Install VS Code and its '{VscodeCommand}' shell command, then restart fleet.";
}
