using Fleet.Features.Setup.RunSetup.Enums;
using Fleet.Features.Setup.RunSetup.Models;

namespace Fleet.Features.Setup.RunSetup;

public sealed class SetupHandler(Func<string, bool> onPath)
{
    public static readonly IReadOnlyList<string> Required = ["wezterm", "git"];

    public static readonly IReadOnlyList<string> Harness = ["nvim", "claude", "yazi"];

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
        WiringState.Added => new SetupStep("fleet wezterm", true, $"wrote {wiring.Path}"),

        WiringState.Already => new SetupStep(
            "fleet wezterm", true, $"already up to date {wiring.Path}"),

        _ => new SetupStep(
            "fleet wezterm",
            false,
            wiring.Detail.Length > 0 ? wiring.Detail : $"could not write {wiring.Path}",
            false,
            $"create {wiring.Path} yourself, or fix the permission problem and rerun setup"),
    };
}
