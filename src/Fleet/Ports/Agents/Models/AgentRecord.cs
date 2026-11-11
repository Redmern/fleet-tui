using Fleet.Shared.Constants;
using Fleet.Shared.Harness;
using Fleet.Shared.Settings;
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
    bool HostInNvim = false,
    bool? InNvim = null,
    string Summary = "",
    string ReportedAt = "",
    RoleModel? Model = null,
    string OrchestratorHarness = "")
{
    public HarnessProfile OrchestratorProfile => HarnessRegistry.For(OrchestratorHarness);

    public bool StartedInNvim => InNvim ?? true;

    public bool RunsHarness => AgentHarness.RunsHarness(Harness);

    public bool HostedInNvim => AgentHarness.PaneInNvim(Harness, HostInNvim);

    public AgentRecord HostedFor(bool nvimAllowed)
    {
        var (harness, host) = AgentHarness.Hosted(Harness, HostInNvim, nvimAllowed);

        return this with { Harness = harness, HostInNvim = host };
    }

    public IReadOnlyList<string> Command(ClaudeLaunch? launch = null) =>
        AgentHarness.AgentCommand(Harness, HostInNvim, launch);

    public IReadOnlyList<string> Binaries() => AgentHarness.Binaries(Harness, HostInNvim);

    public static (string Harness, bool HostInNvim) Migrated(
        string harness, bool? hostInNvim, bool? legacyClaude, string owner) =>
        hostInNvim is { } host
            ? (harness, host)
            : harness.Trim() == AgentHarness.Nvim && (legacyClaude ?? owner.Length > 0)
                ? (AgentHarness.Claude, true)
                : (harness, false);

    public RoleModels ModelsFrom(RoleModels settings, bool orchestrator) =>
        orchestrator
            ? settings with { Sub = Model ?? Concrete(settings.Sub, SettingsDefaults.SubModel) }
            : settings with { Agent = Model ?? Concrete(settings.Agent, SettingsDefaults.AgentModel) };

    private static RoleModel Concrete(RoleModel setting, RoleModel fallback) =>
        setting.IsAuto ? fallback : setting;
}
