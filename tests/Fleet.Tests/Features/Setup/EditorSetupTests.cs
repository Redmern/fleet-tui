using Fleet.Features.Setup.RunSetup;
using Fleet.Shared.Settings;
using Fleet.Shared.Settings.Enums;

namespace Fleet.Tests.Features.Setup;

public class EditorSetupTests
{
    private static readonly string[] Harness = ["git", "nvim", "claude", "yazi"];

    [Fact]
    public void Code_found_on_path_is_an_ok_optional_step()
    {
        var report = new SetupHandler(_ => true).Inspect("c");

        var step = Assert.Single(report.Steps, s => s.Name == EditorChoice.VscodeCommand);

        Assert.True(step.Ok);
        Assert.False(step.Required);
    }

    [Fact]
    public void A_missing_code_is_not_a_to_do_for_an_nvim_user()
    {
        var report = new SetupHandler(t => Harness.Contains(t)).Inspect("c", editor: Editor.Nvim);

        var step = Assert.Single(report.Steps, s => s.Name == EditorChoice.VscodeCommand);

        Assert.True(step.Ok);
        Assert.Contains("vscode", step.Detail);
        Assert.Empty(report.Missing);
    }

    [Fact]
    public void A_missing_code_is_a_to_do_that_does_not_block_when_the_editor_is_vscode()
    {
        var report = new SetupHandler(t => Harness.Contains(t)).Inspect("c", editor: Editor.Vscode);

        var step = Assert.Single(report.Missing);

        Assert.Equal(EditorChoice.VscodeCommand, step.Name);
        Assert.False(step.Required);
        Assert.False(report.Blocked);
        Assert.Equal(SetupHints.For(EditorChoice.VscodeCommand), step.Fix);
    }

    [Fact]
    public void The_vs_code_hint_names_the_winget_id_on_windows_and_the_shell_command_elsewhere()
    {
        var hint = SetupHints.For(EditorChoice.VscodeCommand);

        if (OperatingSystem.IsWindows())
        {
            Assert.Equal("winget install Microsoft.VisualStudioCode", hint);
        }
        else
        {
            Assert.Contains("'code' shell command", hint);
        }
    }

    [Fact]
    public void Doctor_warns_when_the_editor_is_vscode_and_code_is_missing()
    {
        var warning = EditorTrouble.With(Editor.Vscode, codeOnPath: false, windows: true);

        Assert.NotNull(warning);
        Assert.Contains(EditorChoice.InstallHint(windows: true), warning);
    }

    [Theory]
    [InlineData(Editor.Vscode, true)]
    [InlineData(Editor.Nvim, false)]
    [InlineData(Editor.Nvim, true)]
    public void Doctor_says_nothing_when_code_is_there_or_not_needed(Editor editor, bool codeOnPath)
    {
        Assert.Null(EditorTrouble.With(editor, codeOnPath, windows: false));
    }
}
