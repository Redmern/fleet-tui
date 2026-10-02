using Fleet.Ports.Agents.Models;

namespace Fleet.Features.Agents.AutoClose.Models;

public sealed record IdleWatch(
    AgentRecord Agent,
    bool PaneAlive,
    bool Focused,
    bool Working,
    bool AsksTheUser,
    DateTime LastActivity);
