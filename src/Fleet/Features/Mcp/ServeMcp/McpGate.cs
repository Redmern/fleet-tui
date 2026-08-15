using Fleet.Features.Mcp.ServeMcp.Models;
using Fleet.Shared.Settings.Enums;
using Fleet.Shared.Settings.Models;

namespace Fleet.Features.Mcp.ServeMcp;

public static class McpGate
{
    public static GateDecision Decide(HarnessTool tool, SettingsConfig settings)
    {
        if (tool == HarnessTool.None)
        {
            return new GateDecision(ActionPolicy.Forbid, AskChannel.Both);
        }

        var rule = settings.RuleFor(tool);

        return new GateDecision(rule.Policy, rule.Channel);
    }
}
