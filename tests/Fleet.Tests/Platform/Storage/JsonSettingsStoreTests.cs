using Fleet.Platform.Storage;
using Fleet.Shared.Aidlc.Enums;
using Fleet.Shared.Settings.Enums;
using Fleet.Shared.Settings.Models;

namespace Fleet.Tests.Platform.Storage;

[Collection(ConfigHomeCollection.Name)]
public sealed class JsonSettingsStoreTests : ConfigHomeFixture
{
    private static JsonSettingsStore Store => new();

    [Fact]
    public void A_project_with_no_file_gets_the_defaults()
    {
        var config = Store.Load("techweb");

        Assert.Equal(",", config.Trigger);
        Assert.Equal(ActionPolicy.Allow, config.RuleFor(HarnessTool.ListAgents).Policy);
        Assert.Equal(ActionPolicy.Allow, config.RuleFor(HarnessTool.NewAgent).Policy);
        Assert.Equal(ActionPolicy.Ask, config.RuleFor(HarnessTool.StopAgent).Policy);
    }

    [Fact]
    public void A_policy_a_channel_and_a_trigger_round_trip()
    {
        var config = SettingsConfig.Default
            .With(HarnessTool.NewAgent, ActionPolicy.Forbid)
            .With(HarnessTool.StopAgent, AskChannel.FleetDialog)
            .WithTrigger(";");

        Store.Save("techweb", config);

        var loaded = Store.Load("techweb");

        Assert.Equal(";", loaded.Trigger);
        Assert.Equal(ActionPolicy.Forbid, loaded.RuleFor(HarnessTool.NewAgent).Policy);
        Assert.Equal(AskChannel.FleetDialog, loaded.RuleFor(HarnessTool.StopAgent).Channel);
        Assert.Equal(ActionPolicy.Allow, loaded.RuleFor(HarnessTool.ListAgents).Policy);
    }

    [Fact]
    public void The_commit_and_push_gates_default_to_ask()
    {
        var config = Store.Load("techweb");

        Assert.Equal(ActionPolicy.Ask, config.Commit);
        Assert.Equal(ActionPolicy.Ask, config.Push);
    }

    [Fact]
    public void The_commit_and_push_gates_round_trip()
    {
        Store.Save(
            "techweb",
            SettingsConfig.Default.WithCommit(ActionPolicy.Allow).WithPush(ActionPolicy.Forbid));

        var loaded = Store.Load("techweb");

        Assert.Equal(ActionPolicy.Allow, loaded.Commit);
        Assert.Equal(ActionPolicy.Forbid, loaded.Push);
    }

    [Fact]
    public void The_aidlc_mode_defaults_to_off()
    {
        Assert.Equal(AidlcMode.Off, Store.Load("techweb").Aidlc.Mode);
    }

    [Fact]
    public void The_aidlc_mode_round_trips()
    {
        Store.Save("techweb", SettingsConfig.Default.WithAidlcMode(AidlcMode.Manual));

        Assert.Equal(AidlcMode.Manual, Store.Load("techweb").Aidlc.Mode);
    }

    [Fact]
    public void A_default_aidlc_mode_is_written_as_an_empty_value()
    {
        Store.Save("techweb", SettingsConfig.Default.WithAidlcMode(AidlcMode.On).WithAidlcMode(AidlcMode.Off));

        var raw = File.ReadAllText(Path.Combine(FleetPaths.Settings, "techweb.json"));

        Assert.Contains("\"aidlc\": \"\"", raw);
    }

    [Fact]
    public void Every_aidlc_setting_round_trips()
    {
        var aidlc = new AidlcSettings(
            AidlcMode.On, Profile.Feature, Autonomy.Automatic, AidlcPart.PlanGate | AidlcPart.Review | AidlcPart.WalkingSkeleton);

        Store.Save("techweb", SettingsConfig.Default.WithAidlc(aidlc));

        Assert.Equal(aidlc, Store.Load("techweb").Aidlc);
    }

    [Fact]
    public void Default_aidlc_settings_write_nothing_but_empty_values()
    {
        Store.Save("techweb", SettingsConfig.Default);

        var raw = File.ReadAllText(Path.Combine(FleetPaths.Settings, "techweb.json"));

        Assert.Contains("\"aidlcProfile\": \"\"", raw);
        Assert.Contains("\"aidlcAutonomy\": \"\"", raw);
        Assert.Contains("\"aidlcOff\": []", raw);
    }

    [Fact]
    public void Switched_off_parts_are_written_as_words()
    {
        Store.Save("techweb", SettingsConfig.Default.WithAidlc(AidlcSettings.Default.With(AidlcPart.Verify, on: false)));

        var raw = File.ReadAllText(Path.Combine(FleetPaths.Settings, "techweb.json"));

        Assert.Contains("\"verify\"", raw);
    }

    [Fact]
    public void An_old_settings_file_without_the_new_aidlc_fields_gets_their_defaults()
    {
        FleetPaths.EnsureDirs();
        File.WriteAllText(
            Path.Combine(FleetPaths.Settings, "techweb.json"),
            """{"version":1,"trigger":";","aidlc":"manual","tools":{}}""");

        var aidlc = Store.Load("techweb").Aidlc;

        Assert.Equal(AidlcMode.Manual, aidlc.Mode);
        Assert.Equal(Profile.Express, aidlc.DefaultProfile);
        Assert.Equal(Autonomy.Guided, aidlc.Autonomy);
        Assert.Equal(AidlcPart.None, aidlc.Off);
    }

    [Fact]
    public void Unknown_aidlc_words_fall_back_to_the_defaults()
    {
        FleetPaths.EnsureDirs();
        File.WriteAllText(
            Path.Combine(FleetPaths.Settings, "techweb.json"),
            """{"version":1,"aidlc":"sometimes","aidlcProfile":"epic","aidlcAutonomy":"7","aidlcOff":["review","bogus","none"]}""");

        var aidlc = Store.Load("techweb").Aidlc;

        Assert.Equal(AidlcSettings.Default with { Off = AidlcPart.Review }, aidlc);
    }

    [Fact]
    public void Two_projects_keep_separate_files()
    {
        Store.Save("techweb", SettingsConfig.Default.With(HarnessTool.NewAgent, ActionPolicy.Forbid));

        Assert.Equal(ActionPolicy.Forbid, Store.Load("techweb").RuleFor(HarnessTool.NewAgent).Policy);
        Assert.Equal(ActionPolicy.Allow, Store.Load("other").RuleFor(HarnessTool.NewAgent).Policy);
    }

    [Fact]
    public void A_corrupt_file_falls_back_to_the_defaults()
    {
        FleetPaths.EnsureDirs();
        File.WriteAllText(Path.Combine(FleetPaths.Settings, "techweb.json"), "{ this is not json");

        Assert.Equal(",", Store.Load("techweb").Trigger);
    }

    [Fact]
    public void An_unknown_tool_or_policy_word_is_skipped()
    {
        FleetPaths.EnsureDirs();
        File.WriteAllText(
            Path.Combine(FleetPaths.Settings, "techweb.json"),
            """{"version":1,"tools":{"invented_tool":{"policy":"allow"},"new_agent":{"policy":"nonsense"}}}""");

        var config = Store.Load("techweb");

        Assert.Equal(ActionPolicy.Allow, config.RuleFor(HarnessTool.NewAgent).Policy);
    }

    [Fact]
    public void A_project_name_needing_sanitising_still_round_trips()
    {
        Store.Save("te ch/web", SettingsConfig.Default.WithTrigger(":"));

        Assert.Equal(":", Store.Load("te ch/web").Trigger);
    }
}
