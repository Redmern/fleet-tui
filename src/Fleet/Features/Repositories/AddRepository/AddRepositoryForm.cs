namespace Fleet.Features.Repositories.AddRepository;

public static class AddRepositoryForm
{
    public const string NameMissing = "Type a name for the repository first.";

    public const string UrlMissing = "Type or pick a URL to clone from first.";

    public const string BranchMissing = "Type a branch name first.";

    public static string? Validate(bool cloning, string name, string url, string branch)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return NameMissing;
        }

        if (cloning && string.IsNullOrWhiteSpace(url))
        {
            return UrlMissing;
        }

        return string.IsNullOrWhiteSpace(branch) ? BranchMissing : null;
    }
}
