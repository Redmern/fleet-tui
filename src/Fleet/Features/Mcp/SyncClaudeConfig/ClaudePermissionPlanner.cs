using Fleet.Features.Mcp.SyncClaudeConfig.Models;
using Fleet.Shared.Mcp;
using Fleet.Shared.Settings;
using Fleet.Shared.Settings.Enums;
using Fleet.Shared.Settings.Models;

namespace Fleet.Features.Mcp.SyncClaudeConfig;

public static class ClaudePermissionPlanner
{
    public static ClaudePermissions Plan(SettingsConfig settings)
    {
        var allow = new List<string>();
        var deny = new List<string>();
        var ask = new List<string>();

        foreach (var tool in SettingsDefaults.Configurable)
        {
            if (tool == HarnessTool.DeleteWorktree)
            {
                continue;
            }

            var id = McpServerId.RuleId(tool);
            var rule = settings.RuleFor(tool);

            switch (rule.Policy)
            {
                case ActionPolicy.Forbid:
                    deny.Add(id);
                    break;

                case ActionPolicy.Ask when rule.Channel == AskChannel.ClaudePermission:
                    ask.Add(id);
                    break;

                default:
                    allow.Add(id);
                    break;
            }
        }

        Gate(GitGates.CommitRule, settings.Commit, allow, deny, ask);
        Gate(GitGates.PushRule, settings.Push, allow, deny, ask);

        return new ClaudePermissions(allow, deny, ask);
    }

    private static void Gate(
        string rule, ActionPolicy policy, List<string> allow, List<string> deny, List<string> ask)
    {
        switch (policy)
        {
            case ActionPolicy.Forbid:
                deny.Add(rule);
                break;

            case ActionPolicy.Ask:
                ask.Add(rule);
                break;

            default:
                allow.Add(rule);
                break;
        }
    }
}
