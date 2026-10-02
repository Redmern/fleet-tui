using Fleet.Features.Agents.AutoClose.Models;
using Fleet.Ports.Agents.Models;
using Fleet.Ports.Mux.Models;
using Fleet.Shared;
using Fleet.Shared.Constants;
using Fleet.Shared.Settings.Models;

namespace Fleet.Features.Agents.AutoClose;

public static class IdleAgents
{
    public static bool ShouldClose(IdleWatch watch, SettingsConfig settings, string projectRoot, DateTime now) =>
        settings.AutoClose
        && settings.AutoCloseMinutes >= 1
        && watch.PaneAlive
        && !watch.Focused
        && !watch.Working
        && !watch.AsksTheUser
        && !PathKey.Same(watch.Agent.Worktree, projectRoot)
        && Finished(watch.Agent.Status)
        && now - watch.LastActivity >= TimeSpan.FromMinutes(settings.AutoCloseMinutes);

    public static IReadOnlyList<Pane> PanesOf(AgentRecord agent, IEnumerable<Pane> panes) =>
        [.. panes.Where(p => AgentPanes.Owns(p, agent) && !SubBrowse.Is(p))];

    public static bool Focused(IEnumerable<Pane> owned) =>
        owned.Any(p => p.IsActive && !FleetWorkspaces.IsHidden(p.SessionName));

    public static bool Finished(string status) =>
        status.Trim().Length > 0
        && OrchestrationStatus.Normalize(status) is OrchestrationStatus.Done or OrchestrationStatus.Failed;

    public static string Note(string label, string status, TimeSpan idle) =>
        $"auto-closed {label}: {OrchestrationStatus.Normalize(status)} and idle for {(int)idle.TotalMinutes} min; open it again to continue";
}
