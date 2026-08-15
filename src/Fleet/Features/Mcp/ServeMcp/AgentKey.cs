using Fleet.Ports.Agents.Models;

namespace Fleet.Features.Mcp.ServeMcp;

public static class AgentKey
{
    public static AgentRecord? Find(
        IReadOnlyList<AgentRecord> agents, string repository, string branch) =>
        agents.FirstOrDefault(a =>
            string.Equals(a.Repository, repository, StringComparison.OrdinalIgnoreCase)
            && string.Equals(a.Branch, branch, StringComparison.OrdinalIgnoreCase));

    public static string Describe(string repository, string branch) => $"{repository}/{branch}";
}
