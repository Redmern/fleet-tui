namespace Fleet.Platform.Storage;

public static class FileTabStateStore
{
    public static string Glob => Path.Combine(FleetPaths.Requests, "tabstate-*.txt");

    public static void Publish(string project, IReadOnlyDictionary<string, string> states)
    {
        var name = string.Concat(project.Split(Path.GetInvalidFileNameChars())).Trim();

        if (name.Length == 0)
        {
            return;
        }

        var file = Path.Combine(FleetPaths.Requests, $"tabstate-{name}.txt");

        var lines = states
            .Where(s => s.Key.Trim().Length > 0 && s.Value.Trim().Length > 0)
            .Select(s => $"{s.Key.Trim()}\t{s.Value.Trim()}")
            .ToList();

        try
        {
            Directory.CreateDirectory(FleetPaths.Requests);

            var temp = file + ".tmp";

            File.WriteAllLines(temp, lines);
            File.Move(temp, file, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }
}
