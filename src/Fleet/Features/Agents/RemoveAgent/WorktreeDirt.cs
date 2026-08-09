namespace Fleet.Features.Agents.RemoveAgent;

public static class WorktreeDirt
{
    public const string FleetDirectory = ".fleet/";

    public static IReadOnlyList<string> Parse(string porcelain)
    {
        var changed = new List<string>();

        foreach (var line in porcelain.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length <= 3)
            {
                continue;
            }

            var path = line[3..].Trim().Replace('\\', '/');

            if (path.Length == 0 || path.StartsWith(FleetDirectory, StringComparison.Ordinal))
            {
                continue;
            }

            changed.Add(path);
        }

        return changed;
    }
}
