using Fleet.Shared.Settings.Models;

namespace Fleet.Ports.Agents.Models;

public sealed record AgentRecord(
    string Worktree,
    string Repository,
    string Branch,
    string Harness,
    string BaseRef,
    bool RepositoryWasBare,
    bool Hidden = false,
    bool Open = false,
    string Owner = "",
    string Status = "",
    bool? Claude = null,
    bool? InNvim = null,
    string Summary = "",
    string ReportedAt = "",
    RoleModel? Model = null)
{
    public bool RunsClaude => Claude ?? Owner.Length > 0;

    public bool StartedInNvim => InNvim ?? true;

    public RoleModels ModelsFrom(RoleModels settings, bool orchestrator) =>
        Model is null ? settings
        : orchestrator ? settings with { Sub = Model }
        : settings with { Agent = Model };
}
