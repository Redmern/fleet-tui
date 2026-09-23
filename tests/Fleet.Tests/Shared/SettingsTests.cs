using Fleet.Shared.Settings;
using Fleet.Shared.Settings.Enums;
using Fleet.Shared.Settings.Models;

namespace Fleet.Tests.Shared;

public class SettingsTests
{
    [Fact]
    public void The_shipped_defaults_match_the_intended_policy_per_tool()
    {
        var config = SettingsConfig.Default;

        Assert.Equal(ActionPolicy.Allow, config.RuleFor(HarnessTool.ListAgents).Policy);
        Assert.Equal(ActionPolicy.Allow, config.RuleFor(HarnessTool.Report).Policy);
        Assert.Equal(ActionPolicy.Allow, config.RuleFor(HarnessTool.NewAgent).Policy);
        Assert.Equal(ActionPolicy.Allow, config.RuleFor(HarnessTool.TellAgent).Policy);
        Assert.Equal(ActionPolicy.Allow, config.RuleFor(HarnessTool.Dispatch).Policy);

        Assert.Equal(ActionPolicy.Ask, config.RuleFor(HarnessTool.StopAgent).Policy);
        Assert.Equal(ActionPolicy.Ask, config.RuleFor(HarnessTool.DeleteWorktree).Policy);
        Assert.Equal(ActionPolicy.Ask, config.RuleFor(HarnessTool.PullRepository).Policy);

        Assert.Equal(ActionPolicy.Forbid, config.RuleFor(HarnessTool.RemoveRepository).Policy);

        Assert.Equal(ActionPolicy.Ask, config.Commit);
        Assert.Equal(ActionPolicy.Ask, config.Push);
        Assert.Equal(AidlcMode.Off, config.Aidlc);
    }

    [Fact]
    public void The_trigger_defaults_to_a_comma()
    {
        Assert.Equal(",", SettingsConfig.Default.Trigger);
    }

    [Fact]
    public void With_policy_keeps_the_channel_and_vice_versa()
    {
        var config = SettingsConfig.Default
            .With(HarnessTool.NewAgent, ActionPolicy.Forbid)
            .With(HarnessTool.StopAgent, AskChannel.FleetDialog);

        Assert.Equal(ActionPolicy.Forbid, config.RuleFor(HarnessTool.NewAgent).Policy);
        Assert.Equal(AskChannel.Both, config.RuleFor(HarnessTool.NewAgent).Channel);

        Assert.Equal(ActionPolicy.Ask, config.RuleFor(HarnessTool.StopAgent).Policy);
        Assert.Equal(AskChannel.FleetDialog, config.RuleFor(HarnessTool.StopAgent).Channel);
    }

    [Fact]
    public void Every_tool_has_a_wire_id_that_round_trips_and_none_collide()
    {
        var ids = HarnessToolIds.All.Select(HarnessToolIds.For).ToList();

        Assert.DoesNotContain(string.Empty, ids);
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.All(HarnessToolIds.All, t => Assert.Equal(t, HarnessToolIds.Parse(HarnessToolIds.For(t))));

        Assert.All(ids, id => Assert.DoesNotContain(' ', id));
        Assert.Equal(HarnessTool.None, HarnessToolIds.Parse("nonsense"));
    }

    [Fact]
    public void Only_changed_rules_and_a_changed_trigger_are_written()
    {
        var config = SettingsConfig.Default.With(HarnessTool.NewAgent, ActionPolicy.Forbid);

        var diff = SettingsDiff.AgainstDefaults(config.Rules);

        Assert.Equal([HarnessTool.NewAgent], diff.Keys);
        Assert.Equal(string.Empty, SettingsDiff.TriggerAgainstDefault(","));
        Assert.Equal(";", SettingsDiff.TriggerAgainstDefault(";"));
    }

    [Fact]
    public void With_aidlc_mode_keeps_everything_else()
    {
        var config = SettingsConfig.Default.WithAidlcMode(AidlcMode.Manual);

        Assert.Equal(AidlcMode.Manual, config.Aidlc);
        Assert.Equal(SettingsDefaults.Commit, config.Commit);
        Assert.Equal(SettingsDefaults.Trigger, config.Trigger);
    }

    [Fact]
    public void Merging_keeps_the_aidlc_mode()
    {
        var merged = SettingsConfig.Default.WithAidlcMode(AidlcMode.On).MergedOverDefaults();

        Assert.Equal(AidlcMode.On, merged.Aidlc);
    }

    [Fact]
    public void Merging_fills_gaps_from_defaults_and_rejects_an_invalid_trigger()
    {
        var partial = new SettingsConfig(
            "  ",
            new Dictionary<HarnessTool, ToolRule>
            {
                [HarnessTool.NewAgent] = new(ActionPolicy.Forbid, AskChannel.Both),
            },
            SettingsDefaults.Commit,
            SettingsDefaults.Push,
            SettingsDefaults.Aidlc);

        var merged = partial.MergedOverDefaults();

        Assert.Equal(",", merged.Trigger);
        Assert.Equal(ActionPolicy.Forbid, merged.RuleFor(HarnessTool.NewAgent).Policy);
        Assert.Equal(ActionPolicy.Allow, merged.RuleFor(HarnessTool.ListAgents).Policy);
    }

    [Fact]
    public void The_signature_changes_when_anything_changes()
    {
        var before = SettingsConfig.Default.Signature;

        Assert.NotEqual(before, SettingsConfig.Default.With(HarnessTool.NewAgent, ActionPolicy.Forbid).Signature);
        Assert.NotEqual(before, SettingsConfig.Default.WithTrigger(";").Signature);
        Assert.NotEqual(before, SettingsConfig.Default.WithAidlcMode(AidlcMode.On).Signature);
    }

    [Theory]
    [InlineData(",", true)]
    [InlineData(";", true)]
    [InlineData("/", true)]
    [InlineData("a", false)]
    [InlineData("1", false)]
    [InlineData(" ", false)]
    [InlineData("", false)]
    [InlineData(",,", false)]
    public void A_trigger_must_be_one_non_alphanumeric_character(string text, bool valid)
    {
        Assert.Equal(valid, DispatchTrigger.IsValid(text));
    }

    [Fact]
    public void A_captured_key_name_becomes_its_character()
    {
        Assert.Equal(",", DispatchTrigger.FromKeyText("Comma"));
        Assert.Equal(".", DispatchTrigger.FromKeyText("Period"));
        Assert.Equal(";", DispatchTrigger.FromKeyText(";"));
        Assert.Equal(string.Empty, DispatchTrigger.FromKeyText("A"));
    }
}
