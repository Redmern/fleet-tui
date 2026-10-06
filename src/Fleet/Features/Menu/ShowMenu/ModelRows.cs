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

    public static IReadOnlyList<PickerEntry> ModelEntries { get; } =
    [
        new(ModelChoice.Inherit, "no --model: the profile's default"),
        .. Aliases.Select(a => new PickerEntry(a, "latest " + a)),
        new(OtherLabel, "type a model alias or full model ID"),
    ];

    public static IReadOnlyList<PickerEntry> EffortEntries { get; } =
    [
        new(ModelChoice.Inherit, "no --effort: the profile's default"),
        .. ModelChoice.Efforts.Select(e => new PickerEntry(e)),
    ];

    public static int OtherIndex => ModelEntries.Count - 1;

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

    public static int ModelIndex(RoleModel current)
    {
        var model = ModelChoice.Model(current.Model);

        if (model == ModelChoice.Inherit)
        {
            return 0;
        }

        var alias = Aliases.ToList().IndexOf(model);

        return alias >= 0 ? alias + 1 : OtherIndex;
    }

    public static string? ModelAt(int index) =>
        index == 0 ? ModelChoice.Inherit
        : index > 0 && index <= Aliases.Count ? Aliases[index - 1]
        : null;

    public static int EffortIndex(RoleModel current)
    {
        var effort = ModelChoice.Effort(current.Effort);

        return effort == ModelChoice.Inherit ? 0 : ModelChoice.Efforts.ToList().IndexOf(effort) + 1;
    }

    public static string EffortAt(int index) =>
        index > 0 && index <= ModelChoice.Efforts.Count ? ModelChoice.Efforts[index - 1] : ModelChoice.Inherit;

    public static string? Typed(string answer)
    {
        var trimmed = answer.Trim();

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
