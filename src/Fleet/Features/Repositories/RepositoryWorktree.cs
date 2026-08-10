using Fleet.Shared;

namespace Fleet.Features.Repositories;

public static class RepositoryWorktree
{
    public static string For(string container, string branch, Func<string, bool> exists)
    {
        var slug = Path.Combine(container, BranchSlug.Of(branch));

        return exists(slug) ? slug : container;
    }
}
