using Fleet.Shared.Settings.Enums;

namespace Fleet.Features.Mcp.ServeMcp.Models;

public sealed record GateDecision(ActionPolicy Policy, AskChannel Channel)
{
    public bool Allowed => Policy == ActionPolicy.Allow;

    public bool Forbidden => Policy == ActionPolicy.Forbid;

    public bool AsksFleet =>
        Policy == ActionPolicy.Ask
        && Channel is AskChannel.Both or AskChannel.FleetDialog;
}
