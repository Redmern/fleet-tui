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
        var decision = McpGate.Decide(HarnessTool.StopAgent, SettingsConfig.Default);

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
    public void A_sub_orchestrator_may_create_and_manage_agents_without_asking()
    {
        Assert.True(McpGate.Decide(HarnessTool.NewAgent, SettingsConfig.Default, isSub: true).Allowed);
        Assert.True(McpGate.Decide(HarnessTool.TellAgent, SettingsConfig.Default, isSub: true).Allowed);
        Assert.True(McpGate.Decide(HarnessTool.OpenAgent, SettingsConfig.Default, isSub: true).Allowed);
        Assert.True(McpGate.Decide(HarnessTool.StopAgent, SettingsConfig.Default, isSub: true).Allowed);
    }

    [Fact]
    public void The_main_orchestrator_is_still_asked_before_an_ask_tool()
    {
        Assert.False(McpGate.Decide(HarnessTool.StopAgent, SettingsConfig.Default, isSub: false).Allowed);
    }

    [Fact]
    public void A_sub_is_still_refused_a_forbidden_tool()
    {
        var settings = SettingsConfig.Default.With(HarnessTool.NewAgent, ActionPolicy.Forbid);

        Assert.True(McpGate.Decide(HarnessTool.NewAgent, settings, isSub: true).Forbidden);
    }

    [Fact]
    public void A_sub_still_asks_before_destructive_tools_it_is_not_trusted_with()
    {
        var decision = McpGate.Decide(HarnessTool.RemoveAgent, SettingsConfig.Default, isSub: true);

        Assert.False(decision.Allowed);
        Assert.False(decision.Forbidden);
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
