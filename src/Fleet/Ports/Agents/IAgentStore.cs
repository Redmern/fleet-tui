using Fleet.Ports.Agents.Models;

namespace Fleet.Ports.Agents;

public interface IAgentStore
{
    void Save(string project, AgentRecord agent);

    IReadOnlyList<AgentRecord> List(string project);

    void Remove(string project, string worktree);
}
