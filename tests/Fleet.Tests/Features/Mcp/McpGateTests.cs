using Fleet.Features.Mcp.ServeMcp;
using Fleet.Shared.Settings.Enums;
using Fleet.Shared.Settings.Models;

namespace Fleet.Tests.Features.Mcp;

public sealed class McpGateTests
{
    private static SettingsConfig With(HarnessTool tool, ActionPolicy policy, AskChannel channel) =>
        SettingsConfig.Default.With(tool, policy).With(tool, channel);

    [Fact]
    public void An_unknown_tool_is_forbidden()
    {
        var decision = McpGate.Decide(HarnessTool.None, SettingsConfig.Default);

        Assert.True(decision.Forbidden);
    }

    [Fact]
    public void Read_tools_are_allowed_by_default()
    {
        Assert.True(McpGate.Decide(HarnessTool.ListAgents, SettingsConfig.Default).Allowed);
        Assert.True(McpGate.Decide(HarnessTool.Report, SettingsConfig.Default).Allowed);
    }

    [Fact]
    public void Write_tools_ask_by_default()
    {
        var decision = McpGate.Decide(HarnessTool.NewAgent, SettingsConfig.Default);

        Assert.False(decision.Allowed);
        Assert.False(decision.Forbidden);
    }

    [Fact]
    public void A_forbidden_policy_wins_over_any_channel()
    {
        var settings = With(HarnessTool.RemoveRepository, ActionPolicy.Forbid, AskChannel.Both);

        Assert.True(McpGate.Decide(HarnessTool.RemoveRepository, settings).Forbidden);
    }

    [Theory]
    [InlineData(AskChannel.Both, true)]
    [InlineData(AskChannel.FleetDialog, true)]
    [InlineData(AskChannel.ClaudePermission, false)]
    public void Only_the_fleet_channels_raise_the_dashboard_prompt(AskChannel channel, bool asks)
    {
        var settings = With(HarnessTool.NewAgent, ActionPolicy.Ask, channel);

        Assert.Equal(asks, McpGate.Decide(HarnessTool.NewAgent, settings).AsksFleet);
    }

    [Fact]
    public void A_claude_only_ask_does_not_ask_fleet_because_claude_already_prompted()
    {
        var settings = With(HarnessTool.NewAgent, ActionPolicy.Ask, AskChannel.ClaudePermission);

        var decision = McpGate.Decide(HarnessTool.NewAgent, settings);

        Assert.False(decision.AsksFleet);
        Assert.False(decision.Allowed);
        Assert.False(decision.Forbidden);
    }
}
