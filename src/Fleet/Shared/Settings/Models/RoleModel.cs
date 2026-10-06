namespace Fleet.Shared.Settings.Models;

public sealed record RoleModel(string Model, string Effort)
{
    public static RoleModel Inherit { get; } = new(ModelChoice.Inherit, ModelChoice.Inherit);

    public RoleModel Normalized => new(ModelChoice.Model(Model), ModelChoice.Effort(Effort));

    public IReadOnlyList<string> Arguments
    {
        get
        {
            var model = ModelChoice.Model(Model);
            var effort = ModelChoice.Effort(Effort);

            return
            [
                .. model == ModelChoice.Inherit ? [] : new[] { ModelChoice.ModelFlag, model },
                .. effort == ModelChoice.Inherit ? [] : new[] { ModelChoice.EffortFlag, effort },
            ];
        }
    }

    public string Signature => $"{ModelChoice.Model(Model)}:{ModelChoice.Effort(Effort)}";
}
