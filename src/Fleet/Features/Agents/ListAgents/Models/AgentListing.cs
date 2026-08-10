using Fleet.Ports.Agents.Models;

namespace Fleet.Features.Agents.ListAgents.Models;

public sealed record AgentListing(
    IReadOnlyList<AgentRecord> Open,
    IReadOnlyList<AgentRecord> Hidden)
{
    public const int OpenTab = 0;

    public const int HiddenTab = 1;

    public IReadOnlyList<AgentRecord> For(int tab) => tab == HiddenTab ? Hidden : Open;
}
