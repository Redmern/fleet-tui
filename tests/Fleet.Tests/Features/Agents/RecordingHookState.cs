using Fleet.Ports.Agents;
using Fleet.Shared.Status.Models;

namespace Fleet.Tests.Features.Agents;

internal sealed class RecordingHookState : IAgentStateStore
{
    public List<string> Forgotten { get; } = [];

    public List<AgentReport> Reported { get; } = [];

    public Task ReportAsync(AgentReport report, CancellationToken ct = default)
    {
        Reported.Add(report);
        return Task.CompletedTask;
    }

    public Task<AgentSnapshot> GetSnapshotAsync(CancellationToken ct = default) =>
        Task.FromResult(AgentSnapshot.Empty);

    public Task ForgetAsync(string worktree, CancellationToken ct = default)
    {
        Forgotten.Add(worktree);
        return Task.CompletedTask;
    }
}
