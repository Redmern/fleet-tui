using Fleet.Ports;
using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;
using Fleet.Ports.Approvals;
using Fleet.Ports.Mux;
using Fleet.Ports.Projects;
using Fleet.Ports.Projects.Models;
using Fleet.Ports.Remotes;
using Fleet.Ports.Requests;
using Fleet.Ports.Settings;
using Fleet.Shared.Results;

namespace Fleet.Features.Head.ServeHead.Models;

public sealed record HeadDeps(
    IProjectStore Projects,
    IMuxDriver Mux,
    ISettingsStore Settings,
    IApprovalChannel Approvals,
    IAgentStore Agents,
    IActionRequestStore Requests,
    IWorkspaceRequestStore Workspaces,
    Func<Project, CancellationToken, Task<bool>> IsOpen,
    Func<Project, Task<string?>> EnsureOpen,
    Func<string, string?> DashPane,
    IFleetLog Log,
    IRemoteMachines Remotes,
    IKnownRemoteStore KnownRemotes,
    Func<Project, CancellationToken, Task<ProjectStructure>> Structure,
    Func<Project, AgentRecord, bool, CancellationToken, Task<Result<AgentRecord>>>? SetVisible = null);

public sealed record HeadTiming(
    TimeSpan Poll,
    TimeSpan Settle,
    TimeSpan KeyDelay,
    TimeSpan OpenTimeout,
    TimeSpan QueueTimeout)
{
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(60);

    public static HeadTiming Default => new(
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(1),
        TimeSpan.FromMilliseconds(400),
        TimeSpan.FromSeconds(90),
        TimeSpan.FromMinutes(60));
}
