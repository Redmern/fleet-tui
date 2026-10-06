using Fleet.Shared.Settings.Models;

namespace Fleet.Shared.Constants;

public sealed record ClaudeLaunch(string Name, RoleModel Model)
{
    public const string NameFlag = "--name";

    public bool SubagentGuidance { get; init; }

    public IReadOnlyList<string> Arguments
    {
        get
        {
            var name = SessionNames.Part(Name);

            return
            [
                .. name.Length == 0 ? [] : new[] { NameFlag, name },
                .. Model.Arguments,
                .. SubagentGuidance ? new[] { Orchestrations.SubagentGuidance.AppendFlag, Orchestrations.SubagentGuidance.RelativePath } : [],
            ];
        }
    }

    public static ClaudeLaunch Head(RoleModel model) => new(SessionNames.Head, model);

    public static ClaudeLaunch MainOrchestrator(string project, RoleModels models) =>
        new(SessionNames.MainOrchestrator(project), models.Main);

    public static ClaudeLaunch ForAgent(
        string project, string repository, string branch, bool orchestrator, RoleModels models, string? worktree = null) =>
        new(
            SessionNames.ForAgent(project, repository, branch, orchestrator),
            models.ForAgent(orchestrator))
        {
            SubagentGuidance = !orchestrator && worktree is not null && Orchestrations.SubagentGuidance.IsIn(worktree),
        };
}
