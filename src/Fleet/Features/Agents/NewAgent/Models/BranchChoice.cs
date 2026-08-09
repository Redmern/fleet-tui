namespace Fleet.Features.Agents.NewAgent.Models;

public sealed record BranchChoice(string Reference, bool IsRemote)
{
    public string ShortName =>
        IsRemote && Reference.StartsWith("origin/", StringComparison.Ordinal)
            ? Reference["origin/".Length..]
            : Reference;

    public string Label => IsRemote ? $"{Reference}   (remote)" : Reference;
}
