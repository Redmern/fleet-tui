using Fleet.Shared.Keymap.Enums;
using Fleet.Shared.Settings;
using Fleet.Shared.Settings.Models;
using Fleet.Ui.Models;

namespace Fleet.Features.Menu.ShowMenu;

public static class ModelRows
{
    public static IReadOnlyList<FleetAction> Actions { get; } =
    [
        FleetAction.EditHeadModel,
        FleetAction.EditMainModel,
        FleetAction.EditSubModel,
        FleetAction.EditAgentModel,
    ];

    public static IReadOnlyList<string> Aliases { get; } = ["sonnet", "opus", "haiku", "fable"];

    public const string OtherLabel = "other…";

    public static bool AllowsAuto(FleetAction action) =>
        action is FleetAction.EditSubModel or FleetAction.EditAgentModel;

    public static bool Routes(FleetAction action, string model) => AllowsAuto(action) && ModelChoice.IsAuto(model);

    public static IReadOnlyList<string> Choices(FleetAction action) =>
        AllowsAuto(action) ? [ModelChoice.Auto, .. Aliases] : Aliases;

    public static IReadOnlyList<PickerEntry> ModelEntries(FleetAction action) =>
    [
        new(ModelChoice.Inherit, "no --model: the profile's default"),
        .. Choices(action).Select(a => a == ModelChoice.Auto
            ? new PickerEntry(a, "haiku picks the model and effort per task")
            : new PickerEntry(a, "latest " + a)),
        new(OtherLabel, "type a model alias or full model ID"),
    ];

    public static IReadOnlyList<PickerEntry> EffortEntries { get; } =
    [
        new(ModelChoice.Inherit, "no --effort: the profile's default"),
        .. ModelChoice.Efforts.Select(e => new PickerEntry(e)),
    ];

    public static int OtherIndex(FleetAction action) => ModelEntries(action).Count - 1;

    public static bool IsModelRow(FleetAction action) => Actions.Contains(action);

    public static string Title(FleetAction action) => action switch
    {
        FleetAction.EditHeadModel => "Head model",
        FleetAction.EditMainModel => "Main orchestrator model",
        FleetAction.EditSubModel => "Sub-orchestrator model",
        FleetAction.EditAgentModel => "Repo agent model",
        _ => "Model",
    };

    public static RoleModel Current(FleetAction action, SettingsConfig settings, RoleModel head) => action switch
    {
        FleetAction.EditHeadModel => head,
        FleetAction.EditMainModel => settings.Models.Main,
        FleetAction.EditSubModel => settings.Models.Sub,
        FleetAction.EditAgentModel => settings.Models.Agent,
        _ => RoleModel.Inherit,
    };

    public static SettingsConfig With(FleetAction action, SettingsConfig settings, RoleModel model) => action switch
    {
        FleetAction.EditMainModel => settings.WithModels(settings.Models with { Main = model }),
        FleetAction.EditSubModel => settings.WithModels(settings.Models with { Sub = model }),
        FleetAction.EditAgentModel => settings.WithModels(settings.Models with { Agent = model }),
        _ => settings,
    };

    public static int ModelIndex(FleetAction action, RoleModel current)
    {
        var model = ModelChoice.Model(current.Model);

        if (model == ModelChoice.Inherit)
        {
            return 0;
        }

        var choice = Choices(action).ToList().IndexOf(model);

        return choice >= 0 ? choice + 1 : OtherIndex(action);
    }

    public static string? ModelAt(FleetAction action, int index)
    {
        var choices = Choices(action);

        return index == 0 ? ModelChoice.Inherit
            : index > 0 && index <= choices.Count ? choices[index - 1]
            : null;
    }

    public static int EffortIndex(RoleModel current)
    {
        var effort = ModelChoice.Effort(current.Effort);

        return effort == ModelChoice.Inherit ? 0 : ModelChoice.Efforts.ToList().IndexOf(effort) + 1;
    }

    public static string EffortAt(int index) =>
        index > 0 && index <= ModelChoice.Efforts.Count ? ModelChoice.Efforts[index - 1] : ModelChoice.Inherit;

    public static string? Typed(FleetAction action, string answer)
    {
        var trimmed = answer.Trim();

        if (ModelChoice.IsAuto(trimmed))
        {
            return Routes(action, trimmed) ? ModelChoice.Auto : null;
        }

        if (Aliases.FirstOrDefault(a => a.Equals(trimmed, StringComparison.OrdinalIgnoreCase)) is { } alias)
        {
            return alias;
        }

        var model = ModelChoice.Model(trimmed);

        return model != ModelChoice.Inherit || trimmed.Equals(ModelChoice.Inherit, StringComparison.OrdinalIgnoreCase)
            ? model
            : null;
    }

    public static string Value(RoleModel model)
    {
        var name = ModelChoice.Model(model.Model);
        var effort = ModelChoice.Effort(model.Effort);

        return effort == ModelChoice.Inherit ? $"[{name}]" : $"[{name} · {effort}]";
    }
}
