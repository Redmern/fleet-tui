using Fleet.Platform.Storage;
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
        Assert.Equal(AidlcMode.Off, Store.Load("techweb").Aidlc);
    }

    [Fact]
    public void The_aidlc_mode_round_trips()
    {
        Store.Save("techweb", SettingsConfig.Default.WithAidlcMode(AidlcMode.Manual));

        Assert.Equal(AidlcMode.Manual, Store.Load("techweb").Aidlc);
    }

    [Fact]
    public void A_default_aidlc_mode_is_written_as_an_empty_value()
    {
        Store.Save("techweb", SettingsConfig.Default.WithAidlcMode(AidlcMode.On).WithAidlcMode(AidlcMode.Off));

        var raw = File.ReadAllText(Path.Combine(FleetPaths.Settings, "techweb.json"));

        Assert.Contains("\"aidlc\": \"\"", raw);
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
