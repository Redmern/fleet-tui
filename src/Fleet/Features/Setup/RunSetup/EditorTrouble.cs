using Fleet.Shared.Settings;
using Fleet.Shared.Settings.Enums;

namespace Fleet.Features.Setup.RunSetup;

public static class EditorTrouble
{
    public static string? With(Editor resolved, bool codeOnPath, bool windows) =>
        resolved == Editor.Vscode && !codeOnPath
            ? $"warning: the editor is vscode, but {EditorChoice.InstallHint(windows)}"
            : null;
}
