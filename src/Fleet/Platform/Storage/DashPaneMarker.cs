namespace Fleet.Platform.Storage;

public static class DashPaneMarker
{
    public static void Write(string project, string? paneId)
    {
        var file = FileFor(project);

        if (file is null || string.IsNullOrWhiteSpace(paneId))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(FleetPaths.Requests);
            File.WriteAllText(file, paneId.Trim());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    public static string? Read(string project)
    {
        var file = FileFor(project);

        try
        {
            if (file is null || !File.Exists(file))
            {
                return null;
            }

            var id = File.ReadAllText(file).Trim();

            return id.Length == 0 ? null : id;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? FileFor(string project)
    {
        var name = string.Concat(project.Split(Path.GetInvalidFileNameChars())).Trim();

        return name.Length == 0
            ? null
            : Path.Combine(FleetPaths.Requests, $"dash-{name}.pane");
    }
}
