using Fleet.Shared.Settings;
using Fleet.Shared.Settings.Enums;

namespace Fleet.Tests.Shared.Settings;

public class EditorChoiceTests
{
    [Theory]
    [InlineData(Editor.Vscode, Editor.Nvim, true, true, Editor.Vscode)]
    [InlineData(Editor.Nvim, Editor.Vscode, false, true, Editor.Nvim)]
    [InlineData(null, Editor.Vscode, true, false, Editor.Vscode)]
    [InlineData(null, Editor.Nvim, false, true, Editor.Nvim)]
    [InlineData(null, null, true, true, Editor.Nvim)]
    [InlineData(null, null, true, false, Editor.Nvim)]
    [InlineData(null, null, false, true, Editor.Vscode)]
    [InlineData(null, null, false, false, Editor.Nvim)]
    public void The_project_wins_then_the_global_default_then_what_is_installed_then_nvim(
        Editor? project, Editor? global, bool nvim, bool code, Editor expected) =>
        Assert.Equal(expected, EditorChoice.Resolve(project, global, nvim, code));

    [Theory]
    [InlineData("nvim", Editor.Nvim)]
    [InlineData("vscode", Editor.Vscode)]
    [InlineData(" VSCode ", Editor.Vscode)]
    [InlineData("", null)]
    [InlineData("emacs", null)]
    public void A_stored_word_parses_to_an_editor_or_unset(string stored, Editor? expected) =>
        Assert.Equal(expected, EditorChoice.Parse(stored));

    [Theory]
    [InlineData(Editor.Nvim, "nvim")]
    [InlineData(Editor.Vscode, "vscode")]
    [InlineData(null, "")]
    public void An_editor_is_stored_as_its_word_and_unset_as_empty(Editor? editor, string expected) =>
        Assert.Equal(expected, EditorChoice.Word(editor));

    [Fact]
    public void The_install_hint_names_the_winget_id_on_windows()
    {
        Assert.Contains("winget install Microsoft.VisualStudioCode", EditorChoice.InstallHint(windows: true));
        Assert.Contains("code", EditorChoice.InstallHint(windows: false));
        Assert.DoesNotContain("winget", EditorChoice.InstallHint(windows: false));
    }
}
