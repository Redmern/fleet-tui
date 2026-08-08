namespace Fleet.Features.Repositories;

public static class BranchSlug
{
    /// <summary>
    /// Turns a branch name into a directory name: separators become underscores,
    /// so "release/v1" becomes "release_v1".
    /// </summary>
    /// <remarks>
    /// Area-shared rather than in Shared/: more than one slice here needs it, and
    /// nothing outside this area does.
    /// </remarks>
    public static string Of(string branch) => branch.Replace('/', '_').Replace('\\', '_');
}
