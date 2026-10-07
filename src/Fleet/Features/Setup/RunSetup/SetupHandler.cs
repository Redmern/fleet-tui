using Fleet.Features.Setup.RunSetup.Enums;
using Fleet.Features.Setup.RunSetup.Models;
using Fleet.Shared;

namespace Fleet.Features.Setup.RunSetup;

public sealed class SetupHandler(Func<string, bool> onPath)
{
    public static readonly IReadOnlyList<string> Required = ["wezterm", "git"];

    public static readonly IReadOnlyList<string> Harness = ["nvim", "claude", "yazi"];

    public SetupReport Inspect(
        string modulePath, ConfigWiring wiring, string configDirectory, NvimSetup? nvim = null)
    {
        List<SetupStep> steps =
        [
            .. Required.Select(t => Tool(t, required: true)),
            .. Harness.Select(t => Tool(t, required: false)),
            new("fleet.lua", true, modulePath),
            Wiring(wiring),
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
        _ => "missing - agents cannot open it",
    };

    private static SetupStep Wiring(ConfigWiring wiring) => wiring.State switch
    {
        WiringState.Added => new SetupStep("wezterm config", true, $"wired {wiring.Path}"),

        WiringState.Already => new SetupStep("wezterm config", true, $"already wired {wiring.Path}"),

        WiringState.Failed => new SetupStep(
            "wezterm config",
            false,
            wiring.Detail.Length > 0 ? wiring.Detail : $"could not write {wiring.Path}",
            false,
            $"add \"{SetupLines.Require}\" and "
                + $"\"{SetupLines.Apply}\" to {wiring.Path} yourself"),

        _ => new SetupStep(
            "wezterm config",
            false,
            "no wezterm config found",
            false,
            "create ~/.wezterm.lua, then run fleet setup again"),
    };
}
