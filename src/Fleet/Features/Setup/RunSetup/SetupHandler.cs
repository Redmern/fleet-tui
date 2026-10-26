using Fleet.Features.Setup.RunSetup.Models;
using Fleet.Shared;
using Fleet.Shared.Settings;
using Fleet.Shared.Settings.Enums;

namespace Fleet.Features.Setup.RunSetup;

public sealed class SetupHandler(Func<string, bool> onPath)
{
    public static readonly IReadOnlyList<string> Required = ["git"];

    public static readonly IReadOnlyList<string> Harness = ["nvim", "claude", "yazi"];

    public static readonly IReadOnlyList<string> Optional = [EditorChoice.VscodeCommand];

    public SetupReport Inspect(string configDirectory, NvimSetup? nvim = null, Editor editor = Editor.Nvim)
    {
        List<SetupStep> steps =
        [
            .. Required.Select(t => Tool(t, required: true)),
            .. Harness.Select(t => Tool(t, required: false)),
            Code(editor),
            new("config", true, configDirectory),
            .. nvim is null ? [] : new[] { Nvim(nvim) },
        ];

        return new SetupReport(steps);
    }

    public static SetupStep Nvim(NvimSetup nvim) => nvim switch
    {
        { FleetConfig: false } => new SetupStep(NvimStep, true, "your own config (nvim config: user)"),

        { Version: null } => new SetupStep(NvimStep, false, $"nvim not found; fleet's config is in {nvim.Directory}"),

        { Version: { } version } when !NvimVersion.SupportsAppName(version) => new SetupStep(
            NvimStep,
            false,
            $"nvim {version} is older than {NvimVersion.Minimum}, so it reads your own config",
            false,
            "upgrade neovim, or set nvim config to user (fleet menu › settings › fleet config)"),

        { Installed: false } => new SetupStep(
            NvimStep,
            false,
            $"could not write {nvim.Directory} or install its plugins",
            false,
            "check that the folder is writable and git can reach github.com, then run fleet setup again"),

        _ => new SetupStep(NvimStep, true, $"fleet's own, in {nvim.Directory} (nvim {nvim.Version})"),
    };

    private const string NvimStep = "nvim config";

    private SetupStep Code(Editor editor) =>
        editor == Editor.Vscode || onPath(EditorChoice.VscodeCommand)
            ? Tool(EditorChoice.VscodeCommand, required: false)
            : new SetupStep(EditorChoice.VscodeCommand, true, "not found - only needed when the editor is vscode");

    private SetupStep Tool(string tool, bool required) =>
        onPath(tool)
            ? new SetupStep(tool, true, "found on PATH", required)
            : new SetupStep(
                tool,
                false,
                Cost(tool, required),
                required,
                SetupHints.For(tool));

    private static string Cost(string tool, bool required) => (tool, required) switch
    {
        (_, true) => "missing - fleet needs it",
        ("yazi", _) => "missing - no folder picker or file navigator",
        (EditorChoice.VscodeCommand, _) => "missing - the editor is vscode, so agents cannot open their worktrees",
        _ => "missing - agents cannot open it",
    };
}
