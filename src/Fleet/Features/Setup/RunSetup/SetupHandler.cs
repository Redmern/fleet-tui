using Fleet.Features.Setup.RunSetup.Enums;
using Fleet.Features.Setup.RunSetup.Models;

namespace Fleet.Features.Setup.RunSetup;

public sealed class SetupHandler(Func<string, bool> onPath)
{
    public static readonly IReadOnlyList<string> Required = ["wezterm", "git"];

    public static readonly IReadOnlyList<string> Harness = ["nvim", "claude"];

    public SetupReport Inspect(string modulePath, ConfigWiring wiring, string configDirectory)
    {
        List<SetupStep> steps =
        [
            .. Required.Select(t => Tool(t, required: true)),
            .. Harness.Select(t => Tool(t, required: false)),
            new("fleet.lua", true, modulePath),
            Wiring(wiring),
            new("config", true, configDirectory),
        ];

        return new SetupReport(steps);
    }

    private SetupStep Tool(string tool, bool required) =>
        onPath(tool)
            ? new SetupStep(tool, true, "found on PATH", required)
            : new SetupStep(
                tool,
                false,
                required ? "missing - fleet needs it" : "missing - agents cannot open it",
                required,
                SetupHints.For(tool));

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
