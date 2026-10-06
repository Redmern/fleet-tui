using Fleet.Ports.Agents.Models;
using Fleet.Shared.Results;

namespace Fleet.Ports.Agents;

public interface IAgentStarter
{
    Task<Result<string>> StartAsync(AgentStart request, CancellationToken ct = default);

    Task<IReadOnlyCollection<string>> BranchesAsync(string project, string repository, CancellationToken ct = default);
}
