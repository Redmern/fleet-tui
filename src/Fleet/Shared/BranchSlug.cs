namespace Fleet.Shared;

public static class BranchSlug
{
    public static string Of(string branch) => branch.Replace('/', '_').Replace('\\', '_');
}
