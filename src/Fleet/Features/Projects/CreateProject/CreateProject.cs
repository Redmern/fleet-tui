using Fleet.Ports.Projects;
using Fleet.Shared;

namespace Fleet.Features.Projects.CreateProject;

public sealed record CreateProjectCommand(string Name, string Root);

/// <summary>
/// Validation lives here rather than in the dialog, which is what makes it
/// testable with no terminal. The view collects text and shows whatever error
/// comes back.
/// </summary>
public sealed class CreateProjectHandler(IProjectStore store)
{
    public Result<Project> Handle(CreateProjectCommand command)
    {
        var name = command.Name.Trim();
        var root = command.Root.Trim();

        if (name.Length == 0)
        {
            return Result<Project>.Fail("A project name is required.");
        }

        if (root.Length == 0)
        {
            return Result<Project>.Fail("A root directory is required.");
        }

        var sanitized = ProjectName.Sanitize(name);
        if (sanitized.Length == 0)
        {
            return Result<Project>.Fail(
                $"'{name}' contains no usable characters for a project name.");
        }

        if (!Directory.Exists(root))
        {
            return Result<Project>.Fail($"{root} does not exist.");
        }

        try
        {
            store.Save(new Project(sanitized, root));
        }
        catch (Exception e)
            when (e is ArgumentException or DirectoryNotFoundException or IOException
                    or UnauthorizedAccessException)
        {
            return Result<Project>.Fail(e.Message);
        }

        // Read back, so the caller gets exactly what was persisted rather than
        // what we hoped was persisted.
        return store.Load(sanitized) is { } saved
            ? Result<Project>.Ok(saved)
            : Result<Project>.Fail($"'{sanitized}' was written but could not be read back.");
    }
}
