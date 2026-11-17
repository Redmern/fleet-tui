using Fleet.Ports.Agents.Models;

namespace Fleet.Features.Agents.NewAgent.Models;

public sealed record StartedAgent(AgentRecord Agent, string ExistingTip = "", string Tracks = "");
