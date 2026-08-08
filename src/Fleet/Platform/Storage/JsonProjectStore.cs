using System.Text.Json;
using Fleet.Platform.Storage.Models;
using Fleet.Ports.Projects;
using Fleet.Ports.Projects.Models;
using Fleet.Shared;

namespace Fleet.Platform.Storage;

public sealed class JsonProjectStore : IProjectStore
{
    public void Save(Project project)
    {
        var name = ProjectName.Sanitize(project.Name);
        if (name.Length == 0)
        {
            throw new ArgumentException(
                $"'{project.Name}' leaves no usable project name", nameof(project));
        }

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(project.Root));
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"{root} is not a directory");
        }

        FleetPaths.EnsureDirs();

        var file = new ProjectFile { Name = name, Root = HomePath.Contract(root) };
        File.WriteAllText(
            FileFor(name),
            JsonSerializer.Serialize(file, FleetJsonContext.Default.ProjectFile));
    }

    public Project? Load(string name)
    {
        var sanitized = ProjectName.Sanitize(name);
        if (sanitized.Length == 0)
        {
            return null;
        }

        try
        {
            var file = JsonSerializer.Deserialize(
                File.ReadAllText(FileFor(sanitized)),
                FleetJsonContext.Default.ProjectFile);

            if (file is null || string.IsNullOrWhiteSpace(file.Root))
            {
                return null;
            }

            return new Project(
                string.IsNullOrWhiteSpace(file.Name) ? sanitized : file.Name,
                HomePath.Expand(file.Root));
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public IReadOnlyList<Project> List()
    {
        if (!Directory.Exists(FleetPaths.Projects))
        {
            return [];
        }

        return Directory.EnumerateFiles(FleetPaths.Projects, "*.json")
            .Select(f => Load(Path.GetFileNameWithoutExtension(f)))
            .OfType<Project>()
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public void Remove(string name)
    {
        var sanitized = ProjectName.Sanitize(name);
        if (sanitized.Length == 0)
        {
            return;
        }

        try
        {
            File.Delete(FileFor(sanitized));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static string FileFor(string sanitized) =>
        Path.Combine(FleetPaths.Projects, sanitized + ".json");
}
