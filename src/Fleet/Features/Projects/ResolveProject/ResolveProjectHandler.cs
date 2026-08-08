using Fleet.Ports.Projects;
using Fleet.Ports.Projects.Models;

namespace Fleet.Features.Projects.ResolveProject;

public sealed class ResolveProjectHandler(IProjectStore store)
{
    public Project? ForDirectory(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return null;
        }

        string current;

        try
        {
            current = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException)
        {
            return null;
        }

        var projects = store.List();

        Project? best = null;
        var bestLength = -1;

        foreach (var project in projects)
        {
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(project.Root));

            if (!IsWithin(current, root))
            {
                continue;
            }

            if (root.Length > bestLength)
            {
                best = project;
                bestLength = root.Length;
            }
        }

        return best;
    }

    private static bool IsWithin(string candidate, string root)
    {
        if (string.Equals(candidate, root, Comparison))
        {
            return true;
        }

        return candidate.StartsWith(root + Path.DirectorySeparatorChar, Comparison);
    }

    private static StringComparison Comparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
