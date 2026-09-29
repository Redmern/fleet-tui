using Fleet.Ports.Orchestrations;

namespace Fleet.Platform.Storage;

public sealed class FileDispatchHistory : IDispatchHistory
{
    public const int Keep = 20;

    public IReadOnlyList<string> List(string project) =>
        FileFor(project) is { } file ? Read(file, TimeSpan.FromMilliseconds(50)) ?? [] : [];

    public void Add(string project, string prompt)
    {
        var file = FileFor(project);
        var trimmed = prompt.Trim();

        if (file is null || trimmed.Length == 0 || trimmed.Contains('\n')
            || Read(file, BusyFiles.Patience) is not { } history)
        {
            return;
        }

        var next = history
            .Where(l => !string.Equals(l, trimmed, StringComparison.Ordinal))
            .Prepend(trimmed)
            .Take(Keep)
            .ToList();

        BusyFiles.Replace(file, temp => File.WriteAllLines(temp, next));
    }

    private static List<string>? Read(string file, TimeSpan patience) =>
        BusyFiles.Retry(
            () => File.Exists(file)
                ? File.ReadAllLines(file).Where(l => l.Trim().Length > 0).Take(Keep).ToList()
                : [],
            patience);

    private static string? FileFor(string project)
    {
        var name = string.Concat(project.Split(Path.GetInvalidFileNameChars())).Trim();

        return name.Length == 0
            ? null
            : Path.Combine(FleetPaths.Config, "history", name + ".txt");
    }
}
