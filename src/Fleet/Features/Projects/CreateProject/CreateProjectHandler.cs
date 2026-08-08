using Fleet.Features.Projects.CreateProject.Models;
using Fleet.Ports.Projects;
using Fleet.Ports.Projects.Models;
using Fleet.Shared;

namespace Fleet.Features.Projects.CreateProject;

public sealed class CreateProjectHandler(IProjectStore store)
{
    public CreateProjectReply Handle(CreateProjectCommand command)
    {
        var name = command.Name.Trim();
        var root = command.Root.Trim();

        if (name.Length == 0)
        {
            return CreateProjectReply.Rejected("A project name is required.");
        }

        if (root.Length == 0)
        {
            return CreateProjectReply.Rejected("A root directory is required.");
        }

        var sanitized = ProjectName.Sanitize(name);
        if (sanitized.Length == 0)
        {
            return CreateProjectReply.Rejected(
                $"'{name}' contains no usable characters for a project name.");
        }

        string full;
        try
        {
            full = Path.GetFullPath(root);
        }
        catch (Exception e)
            when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return CreateProjectReply.Rejected($"'{root}' is not a valid path.");
        }

        if (!Directory.Exists(full))
        {
            if (!command.CreateRoot)
            {
                return CreateProjectReply.ConfirmRoot(full);
            }

            try
            {
                Directory.CreateDirectory(full);
            }
            catch (Exception e)
                when (e is IOException or UnauthorizedAccessException
                        or PathTooLongException or NotSupportedException)
            {
                return CreateProjectReply.Rejected($"Could not create {full}: {e.Message}");
            }
        }

        try
        {
            store.Save(new Project(sanitized, full));
        }
        catch (Exception e)
            when (e is ArgumentException or DirectoryNotFoundException or IOException
                    or UnauthorizedAccessException)
        {
            return CreateProjectReply.Rejected(e.Message);
        }

        return store.Load(sanitized) is { } saved
            ? CreateProjectReply.Created(saved)
            : CreateProjectReply.Rejected(
                $"'{sanitized}' was written but could not be read back.");
    }
}
