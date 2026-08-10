namespace Fleet.Shared;

public sealed record BranchState(int Ahead, int Behind, bool Dirty)
{
    public static readonly BranchState Unknown = new(0, 0, false);

    public bool Tracked => Ahead > 0 || Behind > 0;
}
