namespace Fleet.Features.Agents.RemoveAgent.Models;

public sealed record WorktreeState(bool Exists, IReadOnlyList<string> Changed)
{
    public static readonly WorktreeState Gone = new(false, []);

    public bool IsDirty => Changed.Count > 0;
}
