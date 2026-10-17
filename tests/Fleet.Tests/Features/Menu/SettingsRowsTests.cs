using Fleet.Features.Menu.EditSettings;
using Fleet.Shared.Settings;
using Fleet.Shared.Settings.Enums;
using Fleet.Shared.Settings.Models;
using Fleet.Ui.Constants;
using Terminal.Gui.Input;

namespace Fleet.Tests.Features.Menu;

public class SettingsRowsTests
{
    [Fact]
    public void The_first_row_is_the_trigger_and_the_rest_are_the_tools()
    {
        var rows = SettingsRows.For(SettingsConfig.Default);

        Assert.Equal(SettingsRows.Count, rows.Count);
        Assert.Contains(SettingsRows.TriggerLabel, rows[0].Text);
        Assert.Contains(",", rows[0].Text);
        Assert.True(SettingsRows.IsTriggerRow(0));
        Assert.Equal(SettingsDefaults.Configurable[0], SettingsRows.ToolAt(1));
    }

    [Fact]
    public void The_policy_is_toned_by_what_it_is()
    {
        var config = SettingsConfig.Default
            .With(HarnessTool.NewAgent, ActionPolicy.Forbid);

        var rows = SettingsRows.For(config);

        var allowRow = rows[SettingsDefaults.Configurable.ToList().IndexOf(HarnessTool.ListAgents) + 1];
        var forbidRow = rows[SettingsDefaults.Configurable.ToList().IndexOf(HarnessTool.NewAgent) + 1];

        Assert.Contains(allowRow.Trailing!, s => s.Tone == FleetTones.Good);
        Assert.Contains(forbidRow.Trailing!, s => s.Tone == FleetTones.Bad);
    }

    [Fact]
    public void The_channel_column_shows_a_dash_unless_the_policy_asks()
    {
        var config = SettingsConfig.Default.With(HarnessTool.NewAgent, ActionPolicy.Allow);

        var allow = SettingsRows.For(config)[SettingsRows.Tools.ToList().IndexOf(HarnessTool.NewAgent) + 1];
        var ask = SettingsRows.For(SettingsConfig.Default.With(HarnessTool.StopAgent, ActionPolicy.Ask))[SettingsRows.Tools.ToList().IndexOf(HarnessTool.StopAgent) + 1];

        Assert.Contains("—", allow.Text);
        Assert.Contains("both", ask.Text);
    }

    [Fact]
    public void Columns_line_up_across_every_row()
    {
        var rows = SettingsRows.For(SettingsConfig.Default);

        var labelWidths = rows.Select(r => r.Spans[0].Text.Length).Distinct();

        Assert.Single(labelWidths);
    }

    [Fact]
    public void The_last_row_switches_status_hooks()
    {
        var last = SettingsRows.Count - 1;

        Assert.True(SettingsRows.IsStatusHooksRow(last));
        Assert.Equal(HarnessTool.None, SettingsRows.ToolAt(last));
        Assert.Contains(SettingsDefaults.StatusHooksLabel, SettingsRows.For(SettingsConfig.Default)[last].Text);
        Assert.Contains("on", SettingsRows.For(SettingsConfig.Default)[last].Text);
        Assert.Contains("off", SettingsRows.For(SettingsConfig.Default.WithStatusHooks(false))[last].Text);
    }

    private static int RowOf(HarnessTool tool) => SettingsRows.Tools.ToList().IndexOf(tool) + 1;

    [Theory]
    [InlineData(ActionPolicy.Allow, ActionPolicy.Ask)]
    [InlineData(ActionPolicy.Ask, ActionPolicy.Forbid)]
    [InlineData(ActionPolicy.Forbid, ActionPolicy.Allow)]
    public void Enter_on_a_tool_row_cycles_its_policy_in_place(ActionPolicy from, ActionPolicy to)
    {
        var config = SettingsConfig.Default.With(HarnessTool.NewAgent, from);

        var next = SettingsRows.Cycled(config, RowOf(HarnessTool.NewAgent));

        Assert.Equal(to, next.RuleFor(HarnessTool.NewAgent).Policy);
    }

    [Fact]
    public void Cycling_to_ask_leaves_the_channel_alone()
    {
        var config = SettingsConfig.Default
            .With(HarnessTool.NewAgent, ActionPolicy.Allow)
            .With(HarnessTool.NewAgent, AskChannel.FleetDialog);

        var next = SettingsRows.Cycled(config, RowOf(HarnessTool.NewAgent));

        Assert.Equal(ActionPolicy.Ask, next.RuleFor(HarnessTool.NewAgent).Policy);
        Assert.Equal(AskChannel.FleetDialog, next.RuleFor(HarnessTool.NewAgent).Channel);
    }

    [Fact]
    public void Cycling_to_ask_keeps_the_default_channel_both()
    {
        var config = SettingsConfig.Default.With(HarnessTool.ListAgents, ActionPolicy.Allow);

        var next = SettingsRows.Cycled(config, RowOf(HarnessTool.ListAgents));

        Assert.Equal(AskChannel.Both, next.RuleFor(HarnessTool.ListAgents).Channel);
    }

    public static TheoryData<Key, ActionPolicy> PolicyKeys => new()
    {
        { Key.A, ActionPolicy.Allow },
        { Key.S, ActionPolicy.Ask },
        { Key.F, ActionPolicy.Forbid },
    };

    [Theory]
    [MemberData(nameof(PolicyKeys))]
    public void The_policy_keys_set_a_tool_row_directly(Key key, ActionPolicy expected)
    {
        var start = expected == ActionPolicy.Forbid ? ActionPolicy.Allow : ActionPolicy.Forbid;
        var config = SettingsConfig.Default.With(HarnessTool.NewAgent, start);

        var policy = SettingsRows.PolicyForKey(key);
        var next = SettingsRows.WithPolicy(config, RowOf(HarnessTool.NewAgent), policy!.Value);

        Assert.Equal(expected, next.RuleFor(HarnessTool.NewAgent).Policy);
    }

    [Fact]
    public void Other_keys_are_not_policy_keys()
    {
        Assert.Null(SettingsRows.PolicyForKey(Key.C));
        Assert.Null(SettingsRows.PolicyForKey(Key.R));
        Assert.Null(SettingsRows.PolicyForKey(Key.A.WithCtrl));
    }

    [Fact]
    public void Enter_on_a_git_gate_cycles_auto_ask_no()
    {
        var rows = Enumerable.Range(0, SettingsRows.Count).ToList();
        var commit = rows.Single(SettingsRows.IsCommitRow);
        var push = rows.Single(SettingsRows.IsPushRow);
        var merge = rows.Single(SettingsRows.IsMergeRow);
        var config = SettingsConfig.Default
            .WithCommit(ActionPolicy.Allow)
            .WithPush(ActionPolicy.Ask)
            .WithMerge(ActionPolicy.Forbid);

        var next = SettingsRows.Cycled(SettingsRows.Cycled(SettingsRows.Cycled(config, commit), push), merge);

        Assert.Equal(ActionPolicy.Ask, next.Commit);
        Assert.Equal(ActionPolicy.Forbid, next.Push);
        Assert.Equal(ActionPolicy.Allow, next.Merge);
    }

    [Fact]
    public void Enter_on_the_status_hooks_row_toggles_it()
    {
        var hooks = Enumerable.Range(0, SettingsRows.Count).Single(SettingsRows.IsStatusHooksRow);
        var config = SettingsConfig.Default.WithStatusHooks(true);

        Assert.False(SettingsRows.Cycled(config, hooks).StatusHooks);
        Assert.True(SettingsRows.Cycled(config.WithStatusHooks(false), hooks).StatusHooks);
    }

    [Fact]
    public void The_trigger_row_does_not_cycle()
    {
        var config = SettingsConfig.Default;

        Assert.Equal(config, SettingsRows.Cycled(config, 0));
    }

    [Theory]
    [InlineData(AskChannel.Both, AskChannel.FleetDialog)]
    [InlineData(AskChannel.FleetDialog, AskChannel.ClaudePermission)]
    [InlineData(AskChannel.ClaudePermission, AskChannel.Both)]
    public void The_channel_cycles_both_fleet_dialog_claude(AskChannel from, AskChannel to)
    {
        Assert.Equal(to, SettingsRows.NextChannel(from));
    }

    [Fact]
    public void C_on_an_ask_row_cycles_and_keeps_its_channel()
    {
        var config = SettingsConfig.Default
            .With(HarnessTool.NewAgent, ActionPolicy.Ask)
            .With(HarnessTool.NewAgent, AskChannel.Both);

        var next = SettingsRows.WithNextChannel(config, RowOf(HarnessTool.NewAgent));

        Assert.Equal(AskChannel.FleetDialog, next.RuleFor(HarnessTool.NewAgent).Channel);
        Assert.Equal(ActionPolicy.Ask, next.RuleFor(HarnessTool.NewAgent).Policy);
    }

    [Theory]
    [InlineData(ActionPolicy.Allow)]
    [InlineData(ActionPolicy.Forbid)]
    public void C_on_a_non_ask_row_changes_nothing_and_says_why(ActionPolicy policy)
    {
        var config = SettingsConfig.Default.With(HarnessTool.NewAgent, policy);

        Assert.Equal(config, SettingsRows.WithNextChannel(config, RowOf(HarnessTool.NewAgent)));
        Assert.Equal(
            "The channel only matters when the policy is ask.",
            SettingsRows.ChannelRefusal(config, RowOf(HarnessTool.NewAgent)));
    }

    [Fact]
    public void C_on_an_ask_row_is_not_refused()
    {
        var config = SettingsConfig.Default.With(HarnessTool.NewAgent, ActionPolicy.Ask);

        Assert.Null(SettingsRows.ChannelRefusal(config, RowOf(HarnessTool.NewAgent)));
    }

    [Fact]
    public void C_on_a_gate_row_changes_nothing()
    {
        var commit = SettingsRows.Count - 4;
        var config = SettingsConfig.Default.WithCommit(ActionPolicy.Ask);

        Assert.Equal(config, SettingsRows.WithNextChannel(config, commit));
        Assert.NotNull(SettingsRows.ChannelRefusal(config, commit));
    }

    [Theory]
    [InlineData(AskChannel.Both, "fleet")]
    [InlineData(AskChannel.FleetDialog, "fleet")]
    [InlineData(AskChannel.ClaudePermission, "Claude")]
    public void Focusing_an_ask_row_explains_where_it_asks(AskChannel channel, string mentions)
    {
        var config = SettingsConfig.Default
            .With(HarnessTool.NewAgent, ActionPolicy.Ask)
            .With(HarnessTool.NewAgent, channel);

        var hint = SettingsRows.Explain(config, RowOf(HarnessTool.NewAgent));

        Assert.Contains(mentions, hint);
        Assert.Contains("Press c to change where it asks.", hint);
    }

    [Fact]
    public void Focusing_a_non_ask_row_explains_nothing()
    {
        var config = SettingsConfig.Default.With(HarnessTool.NewAgent, ActionPolicy.Allow);

        Assert.Equal(string.Empty, SettingsRows.Explain(config, RowOf(HarnessTool.NewAgent)));
        Assert.Equal(string.Empty, SettingsRows.Explain(config, 0));
    }

    [Fact]
    public void The_channel_chip_is_labelled_ask_where()
    {
        Assert.Equal("ask where", SettingsRows.ChannelChip);
    }
}
