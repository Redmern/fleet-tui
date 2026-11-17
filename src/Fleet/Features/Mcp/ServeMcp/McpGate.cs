using Fleet.Features.Mcp.ServeMcp.Models;
using Fleet.Ports.Mcp.Models;
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
        HarnessTool.WaitForAgent,
    ];

    private static readonly HashSet<HarnessTool> AlwaysAsked = [HarnessTool.OpenPr];

    public static IReadOnlyList<HarnessTool> Gated(HarnessTool tool, McpRequest request) =>
        tool == HarnessTool.Dispatch && ToolArguments.Text(request, ToolArguments.Repository).Length > 0
            ? [HarnessTool.Dispatch, HarnessTool.NewAgent]
            : [tool];

    public static GateDecision Decide(HarnessTool tool, SettingsConfig settings, bool isSub = false)
    {
        if (tool == HarnessTool.None)
        {
            return new GateDecision(ActionPolicy.Forbid, AskChannel.Both);
        }

        var rule = settings.RuleFor(tool);

        if (AlwaysAsked.Contains(tool))
        {
            return new GateDecision(
                rule.Policy == ActionPolicy.Forbid ? ActionPolicy.Forbid : ActionPolicy.Ask, AskChannel.Both);
        }

        if (isSub && rule.Policy == ActionPolicy.Ask && SubAutonomous.Contains(tool))
        {
            return new GateDecision(ActionPolicy.Allow, rule.Channel);
        }

        return new GateDecision(rule.Policy, rule.Channel);
    }
}
