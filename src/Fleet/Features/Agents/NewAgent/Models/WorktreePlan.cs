namespace Fleet.Features.Agents.NewAgent.Models;

public sealed record WorktreePlan(string TargetDirectory, bool MustCreate, string Anchor);
