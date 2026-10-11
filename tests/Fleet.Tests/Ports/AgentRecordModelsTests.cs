using Fleet.Ports.Agents.Models;
using Fleet.Shared.Settings;
using Fleet.Shared.Settings.Models;

namespace Fleet.Tests.Ports;

public sealed class AgentRecordModelsTests
{
    private static readonly RoleModel Auto = new(ModelChoice.Auto, "medium");

    private static AgentRecord Record(RoleModel? model = null) =>
        new("/w", "backend", "dev", "claude", "origin/main", false, Model: model);

    [Fact]
    public void An_orchestrator_with_no_stored_model_on_auto_runs_on_the_sub_default()
    {
        var models = Record().ModelsFrom(SettingsDefaults.Models with { Sub = Auto }, orchestrator: true);

        Assert.Equal(new RoleModel("sonnet", "medium"), models.Sub);
    }

    [Fact]
    public void An_agent_with_no_stored_model_on_auto_runs_on_the_agent_default()
    {
        var models = Record().ModelsFrom(SettingsDefaults.Models with { Agent = Auto }, orchestrator: false);

        Assert.Equal(RoleModel.Inherit, models.Agent);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_stored_model_wins_over_auto(bool orchestrator)
    {
        var settings = SettingsDefaults.Models with { Sub = Auto, Agent = Auto };

        var models = Record(new RoleModel("opus", "high")).ModelsFrom(settings, orchestrator);

        Assert.Equal(new RoleModel("opus", "high"), models.ForAgent(orchestrator));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_stored_model_wins_over_a_fixed_setting(bool orchestrator)
    {
        var models = Record(new RoleModel("opus", "high")).ModelsFrom(SettingsDefaults.Models, orchestrator);

        Assert.Equal(new RoleModel("opus", "high"), models.ForAgent(orchestrator));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_record_with_no_stored_model_follows_a_fixed_setting(bool orchestrator)
    {
        var settings = SettingsDefaults.Models with { Sub = new("haiku", "low"), Agent = new("opus", "max") };

        Assert.Equal(settings, Record().ModelsFrom(settings, orchestrator));
    }
}
