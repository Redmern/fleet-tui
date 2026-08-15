using Fleet.Ports.Agents.Models;

namespace Fleet.Features.Orchestrations.ListSubs.Models;

public sealed record SubEntry(AgentRecord Agent, bool IsChild, string Group);

public sealed record SubListing(
    IReadOnlyList<AgentRecord> Board, IReadOnlyList<SubEntry> Flat);
