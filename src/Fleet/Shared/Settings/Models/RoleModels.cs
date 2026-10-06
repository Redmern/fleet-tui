namespace Fleet.Shared.Settings.Models;

public sealed record RoleModels(RoleModel Main, RoleModel Sub, RoleModel Agent)
{
    public RoleModel ForAgent(bool orchestrator) => orchestrator ? Sub : Agent;

    public string Signature => $"main={Main.Signature}|sub={Sub.Signature}|agent={Agent.Signature}";
}
