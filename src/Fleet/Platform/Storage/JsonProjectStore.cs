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

        var file = new ProjectFile
        {
            Name = name,
            Root = HomePath.Contract(root),
            ClaudeProfile = string.IsNullOrWhiteSpace(project.ClaudeProfile) ? null : project.ClaudeProfile.Trim(),
            ForwardPorts = project.ForwardPorts is { Count: > 0 } ports ? [.. ports.Where(IsPort).Distinct()] : null,
            RunCommand = string.IsNullOrWhiteSpace(project.RunCommand) ? null : project.RunCommand.Trim(),
            ReadyPort = project.ReadyPort is { } ready && IsPort(ready) ? ready : null,
            HealthPath = string.IsNullOrWhiteSpace(project.HealthPath) ? null : project.HealthPath.Trim(),
        };
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
                HomePath.Expand(file.Root),
                string.IsNullOrWhiteSpace(file.ClaudeProfile) ? null : file.ClaudeProfile,
                file.ForwardPorts is { Count: > 0 } ports ? [.. ports.Where(IsPort).Distinct()] : null,
                string.IsNullOrWhiteSpace(file.RunCommand) ? null : file.RunCommand,
                file.ReadyPort is { } ready && IsPort(ready) ? ready : null,
                string.IsNullOrWhiteSpace(file.HealthPath) ? null : file.HealthPath);
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

    private static bool IsPort(int port) => port is > 0 and <= 65535;

    private static string FileFor(string sanitized) =>
        Path.Combine(FleetPaths.Projects, sanitized + ".json");
}
