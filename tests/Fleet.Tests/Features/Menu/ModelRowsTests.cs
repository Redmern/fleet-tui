using Fleet.Cli.Commands;
using Fleet.Features.Menu.ShowMenu;
using Fleet.Shared.Keymap;
using Fleet.Shared.Keymap.Enums;
using Fleet.Shared.Settings;
using Fleet.Shared.Settings.Models;

namespace Fleet.Tests.Features.Menu;

public class ModelRowsTests
{
    [Fact]
    public void Fleet_config_has_a_models_section_just_above_permissions()
    {
        var models = FleetMenus.FleetConfig[^2];

        Assert.Equal("models", models.Header);
        Assert.Equal(
            [FleetAction.EditHeadModel, FleetAction.EditMainModel, FleetAction.EditSubModel, FleetAction.EditAgentModel],
            models.Actions);
        Assert.Equal("permissions", FleetMenus.FleetConfig[^1].Header);
    }

    [Theory]
    [InlineData(FleetAction.EditHeadModel)]
    [InlineData(FleetAction.EditMainModel)]
    [InlineData(FleetAction.EditSubModel)]
    [InlineData(FleetAction.EditAgentModel)]
    public void A_model_row_goes_back_to_fleet_config_and_is_a_picker_not_a_toggle(FleetAction action)
    {
        Assert.Equal(FleetAction.OpenFleetConfigMenu, MenuCommand.Parent(action));
        Assert.False(FleetMenus.IsToggle(action));
        Assert.Equal(action, FleetActionIds.Parse(FleetActionIds.For(action)));
    }

    [Fact]
    public void The_rows_show_the_current_model_and_effort()
    {
        var settings = SettingsConfig.Default.WithModels(
            SettingsDefaults.Models with { Agent = new RoleModel("claude-opus-5-5", ModelChoice.Inherit) });

        Assert.Equal("[inherit]", FleetMenus.Value(FleetAction.EditMainModel, settings));
        Assert.Equal("[sonnet · medium]", FleetMenus.Value(FleetAction.EditSubModel, settings));
        Assert.Equal("[claude-opus-5-5]", FleetMenus.Value(FleetAction.EditAgentModel, settings));
        Assert.Equal("[inherit]", FleetMenus.Value(FleetAction.EditHeadModel, settings));
        Assert.Equal(
            "[opus · high]",
            FleetMenus.Value(FleetAction.EditHeadModel, settings, new RoleModel("opus", "high")));
    }

    [Fact]
    public void The_pickers_start_on_the_current_choice()
    {
        var main = FleetAction.EditMainModel;

        Assert.Equal(0, ModelRows.ModelIndex(main, RoleModel.Inherit));
        Assert.Equal(1, ModelRows.ModelIndex(main, new RoleModel("sonnet", "medium")));
        Assert.Equal(ModelRows.OtherIndex(main), ModelRows.ModelIndex(main, new RoleModel("claude-opus-5-5", "low")));
        Assert.Equal(0, ModelRows.EffortIndex(RoleModel.Inherit));
        Assert.Equal("medium", ModelRows.EffortAt(ModelRows.EffortIndex(new RoleModel("sonnet", "medium"))));
    }

    [Fact]
    public void Every_picker_row_maps_back_to_a_value()
    {
        var main = FleetAction.EditMainModel;

        Assert.Equal(ModelChoice.Inherit, ModelRows.ModelAt(main, 0));
        Assert.Equal(["sonnet", "opus", "haiku", "fable"], Enumerable.Range(1, 4).Select(i => ModelRows.ModelAt(main, i)));
        Assert.Null(ModelRows.ModelAt(main, ModelRows.OtherIndex(main)));
        Assert.Equal(
            [ModelChoice.Inherit, .. ModelChoice.Efforts],
            Enumerable.Range(0, ModelRows.EffortEntries.Count).Select(ModelRows.EffortAt));
    }

    [Theory]
    [InlineData(" claude-opus-5-5 ", "claude-opus-5-5")]
    [InlineData("Inherit", "inherit")]
    [InlineData("Opus", "opus")]
    [InlineData("opus'; x", null)]
    [InlineData("", null)]
    public void A_typed_model_is_accepted_only_when_fleet_can_pass_it(string typed, string? expected)
    {
        Assert.Equal(expected, ModelRows.Typed(FleetAction.EditSubModel, typed));
    }

    [Theory]
    [InlineData(FleetAction.EditSubModel)]
    [InlineData(FleetAction.EditAgentModel)]
    public void The_sub_and_agent_rows_offer_auto(FleetAction action)
    {
        var models = ModelRows.ModelEntries(action).Select(e => e.Label).ToList();

        Assert.Contains(ModelChoice.Auto, models);
        Assert.Equal(ModelChoice.Auto, ModelRows.ModelAt(action, models.IndexOf(ModelChoice.Auto)));
        Assert.Equal(models.IndexOf(ModelChoice.Auto), ModelRows.ModelIndex(action, new RoleModel("AUTO", ModelChoice.Inherit)));
        Assert.Equal(ModelChoice.Auto, ModelRows.Typed(action, " Auto "));
        Assert.True(ModelRows.Routes(action, ModelChoice.Auto));
    }

    [Theory]
    [InlineData(FleetAction.EditHeadModel, false)]
    [InlineData(FleetAction.EditMainModel, false)]
    [InlineData(FleetAction.EditSubModel, true)]
    [InlineData(FleetAction.EditAgentModel, true)]
    public void Only_the_sub_and_agent_rows_allow_auto_and_other_is_always_the_last_entry(FleetAction action, bool allows)
    {
        Assert.Equal(allows, ModelRows.AllowsAuto(action));
        Assert.Equal(ModelRows.OtherLabel, ModelRows.ModelEntries(action)[ModelRows.OtherIndex(action)].Label);
        Assert.Equal(ModelRows.ModelEntries(action).Count - 1, ModelRows.OtherIndex(action));
    }

    [Theory]
    [InlineData(FleetAction.EditHeadModel)]
    [InlineData(FleetAction.EditMainModel)]
    public void The_head_and_main_rows_do_not_offer_auto(FleetAction action)
    {
        Assert.DoesNotContain(ModelChoice.Auto, ModelRows.ModelEntries(action).Select(e => e.Label));
        Assert.Null(ModelRows.Typed(action, "auto"));
        Assert.False(ModelRows.Routes(action, ModelChoice.Auto));
    }

    [Fact]
    public void An_auto_row_shows_auto_without_an_effort()
    {
        var settings = SettingsConfig.Default.WithModels(
            SettingsDefaults.Models with { Sub = new RoleModel(ModelChoice.Auto, ModelChoice.Inherit) });

        Assert.Equal("[auto]", FleetMenus.Value(FleetAction.EditSubModel, settings));
    }

    [Fact]
    public void Saving_a_row_changes_only_its_role()
    {
        var model = new RoleModel("haiku", "low");

        Assert.Equal(model, ModelRows.With(FleetAction.EditMainModel, SettingsConfig.Default, model).Models.Main);
        Assert.Equal(model, ModelRows.With(FleetAction.EditSubModel, SettingsConfig.Default, model).Models.Sub);
        Assert.Equal(
            SettingsDefaults.Models with { Agent = model },
            ModelRows.With(FleetAction.EditAgentModel, SettingsConfig.Default, model).Models);
        Assert.Equal(SettingsDefaults.Models, ModelRows.With(FleetAction.EditHeadModel, SettingsConfig.Default, model).Models);
    }
}
