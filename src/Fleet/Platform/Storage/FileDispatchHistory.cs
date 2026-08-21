using Fleet.Ports.Orchestrations;

namespace Fleet.Platform.Storage;

public sealed class FileDispatchHistory : IDispatchHistory
{
    public const int Keep = 20;

    public IReadOnlyList<string> List(string project)
    {
        var file = FileFor(project);

        if (file is null || !File.Exists(file))
        {
            return [];
        }

        try
        {
            return [.. File.ReadAllLines(file).Where(l => l.Trim().Length > 0).Take(Keep)];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public void Add(string project, string prompt)
    {
        var file = FileFor(project);
        var trimmed = prompt.Trim();

        if (file is null || trimmed.Length == 0 || trimmed.Contains('\n'))
        {
            return;
        }

        var next = List(project)
            .Where(l => !string.Equals(l, trimmed, StringComparison.Ordinal))
            .Prepend(trimmed)
            .Take(Keep)
            .ToList();

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);

            var temp = file + ".tmp";

            File.WriteAllLines(temp, next);
            File.Move(temp, file, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static string? FileFor(string project)
    {
        var name = string.Concat(project.Split(Path.GetInvalidFileNameChars())).Trim();

        return name.Length == 0
            ? null
            : Path.Combine(FleetPaths.Config, "history", name + ".txt");
    }
}
