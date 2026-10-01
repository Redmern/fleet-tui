using Fleet.Ports.Projects.Models;
using Fleet.Shared;

namespace Fleet.Platform.Profiles;

public static class PaneProfile
{
    public static IReadOnlyDictionary<string, string>? Env(
        string cwd, IReadOnlyList<Project> projects, AccountProfiles? profiles)
    {
        if (profiles is null || cwd.Length == 0)
        {
            return null;
        }

        var project = projects
            .Where(p => PathKey.Within(cwd, p.Root))
            .OrderByDescending(p => PathKey.For(p.Root).Length)
            .FirstOrDefault();

        if (project?.ClaudeProfile is { Length: > 0 } pinned && profiles.Named(pinned) is { } chosen)
        {
            return AccountProfiles.PinnedEnv(chosen);
        }

        return AccountProfiles.FolderEnv(profiles.ForFolder(cwd));
    }
}
