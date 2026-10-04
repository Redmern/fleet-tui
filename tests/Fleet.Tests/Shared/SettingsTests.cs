using Fleet.Shared.Aidlc.Enums;
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
        Assert.Equal(ActionPolicy.Allow, config.Merge);
        Assert.Equal(AidlcMode.Off, config.Aidlc.Mode);
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

        Assert.Equal(AidlcMode.Manual, config.Aidlc.Mode);
        Assert.Equal(SettingsDefaults.Commit, config.Commit);
        Assert.Equal(SettingsDefaults.Trigger, config.Trigger);
    }

    [Fact]
    public void Merging_keeps_the_aidlc_mode()
    {
        var merged = SettingsConfig.Default.WithAidlcMode(AidlcMode.On).MergedOverDefaults();

        Assert.Equal(AidlcMode.On, merged.Aidlc.Mode);
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
        Assert.NotEqual(before, SettingsConfig.Default.WithStatusHooks(false).Signature);
        Assert.NotEqual(before, SettingsConfig.Default.WithMerge(ActionPolicy.Forbid).Signature);
    }

    [Fact]
    public void The_merge_gate_survives_a_merge_over_the_defaults()
    {
        Assert.Equal(
            ActionPolicy.Ask,
            SettingsConfig.Default.WithMerge(ActionPolicy.Ask).MergedOverDefaults().Merge);
    }

    [Fact]
    public void Status_hooks_are_on_by_default_and_survive_a_merge_over_the_defaults()
    {
        Assert.True(SettingsConfig.Default.StatusHooks);
        Assert.False(SettingsConfig.Default.WithStatusHooks(false).MergedOverDefaults().StatusHooks);
    }

    [Fact]
    public void Aidlc_defaults_to_off_express_guided_with_every_part_on()
    {
        var aidlc = SettingsConfig.Default.Aidlc;

        Assert.Equal(AidlcMode.Off, aidlc.Mode);
        Assert.Equal(Profile.Express, aidlc.DefaultProfile);
        Assert.Equal(Autonomy.Guided, aidlc.Autonomy);
        Assert.All(AidlcSettings.Parts, p => Assert.True(aidlc.IsOn(p)));
    }

    [Fact]
    public void An_aidlc_part_switches_off_and_back_on_without_touching_the_others()
    {
        var off = AidlcSettings.Default.With(AidlcPart.Review, on: false).With(AidlcPart.SpecGate, on: false);

        Assert.False(off.IsOn(AidlcPart.Review));
        Assert.False(off.IsOn(AidlcPart.SpecGate));
        Assert.True(off.IsOn(AidlcPart.Verify));

        var back = off.With(AidlcPart.Review, on: true);

        Assert.True(back.IsOn(AidlcPart.Review));
        Assert.False(back.IsOn(AidlcPart.SpecGate));
    }

    [Fact]
    public void The_aidlc_settings_resolve_a_plan_for_a_profile()
    {
        var plan = AidlcSettings.Default.With(AidlcPart.Verify, on: false).Plan(Profile.Bugfix);

        Assert.Equal(Profile.Bugfix, plan.Profile);
        Assert.Equal([Stage.Verify], plan.Skipped.Select(s => s.Stage));
    }

    [Fact]
    public void The_signature_changes_with_every_aidlc_setting()
    {
        var before = SettingsConfig.Default.Signature;
        var aidlc = SettingsConfig.Default.Aidlc;

        Assert.NotEqual(before, SettingsConfig.Default.WithAidlc(aidlc with { DefaultProfile = Profile.Feature }).Signature);
        Assert.NotEqual(before, SettingsConfig.Default.WithAidlc(aidlc with { Autonomy = Autonomy.Automatic }).Signature);

        Assert.All(
            AidlcSettings.Parts,
            p => Assert.NotEqual(before, SettingsConfig.Default.WithAidlc(aidlc.With(p, on: false)).Signature));
    }

    [Fact]
    public void Merging_keeps_every_aidlc_setting()
    {
        var aidlc = new AidlcSettings(AidlcMode.Manual, Profile.Refactor, Autonomy.Automatic, AidlcPart.Learn);

        Assert.Equal(aidlc, SettingsConfig.Default.WithAidlc(aidlc).MergedOverDefaults().Aidlc);
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

    [Fact]
    public void Both_nvim_settings_default_on_and_each_changes_the_signature()
    {
        var main = SettingsConfig.Default.WithMainOrchestratorInNvim(false);
        var subs = SettingsConfig.Default.WithSubOrchestratorsInNvim(false);

        Assert.True(SettingsConfig.Default.MainOrchestratorInNvim);
        Assert.True(SettingsConfig.Default.SubOrchestratorsInNvim);
        Assert.NotEqual(SettingsConfig.Default.Signature, main.Signature);
        Assert.NotEqual(SettingsConfig.Default.Signature, subs.Signature);
        Assert.NotEqual(main.Signature, subs.Signature);
        Assert.False(main.MergedOverDefaults().MainOrchestratorInNvim);
        Assert.True(main.MergedOverDefaults().SubOrchestratorsInNvim);
        Assert.False(subs.MergedOverDefaults().SubOrchestratorsInNvim);
        Assert.True(subs.MergedOverDefaults().MainOrchestratorInNvim);
    }
}
