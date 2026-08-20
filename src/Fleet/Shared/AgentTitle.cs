namespace Fleet.Shared;

public static class AgentTitle
{
    public static string For(string repository, string branch) =>
        string.IsNullOrEmpty(repository)
            ? BranchSlug.Of(branch)
            : $"{repository}/{BranchSlug.Of(branch)}";
}
