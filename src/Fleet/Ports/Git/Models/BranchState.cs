namespace Fleet.Ports.Git.Models;

public sealed record BranchState(int Ahead, bool Dirty)
{
    public static readonly BranchState Unknown = new(0, false);
}
