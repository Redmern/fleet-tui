using Fleet.Shared.Settings.Models;

namespace Fleet.Shared.Orchestrations;

public sealed record ModelRoutingOutcome(RoleModel Model, string? LogLine);
