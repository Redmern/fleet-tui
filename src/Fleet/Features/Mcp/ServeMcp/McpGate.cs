using Fleet.Features.Mcp.ServeMcp.Models;
using Fleet.Shared.Settings.Enums;
using Fleet.Shared.Settings.Models;

namespace Fleet.Features.Mcp.ServeMcp;

public static class McpGate
{
    private static readonly HashSet<HarnessTool> SubAutonomous =
    [
        HarnessTool.NewAgent,
        HarnessTool.TellAgent,
        HarnessTool.OpenAgent,
        HarnessTool.SetAgentVisible,
        HarnessTool.StopAgent,
    ];

    public static GateDecision Decide(HarnessTool tool, SettingsConfig settings, bool isSub = false)
    {
        if (tool == HarnessTool.None)
        {
            return new GateDecision(ActionPolicy.Forbid, AskChannel.Both);
        }

        var rule = settings.RuleFor(tool);

        if (isSub && rule.Policy == ActionPolicy.Ask && SubAutonomous.Contains(tool))
        {
            return new GateDecision(ActionPolicy.Allow, rule.Channel);
        }

        return new GateDecision(rule.Policy, rule.Channel);
    }
}
