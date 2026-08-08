using Fleet.Ports.Projects;
using Fleet.Shared;

namespace Fleet.Features.Projects.CreateProject;

/// <param name="CreateRoot">
/// Whether a missing root directory may be created. False on the first attempt:
/// the handler reports <see cref="CreateProjectStatus.NeedsRootConfirmation"/> and
/// the caller re-submits with this set once the user has agreed. Creating
/// directories on disk is not something to do on a typo.
/// </param>
public sealed record CreateProjectCommand(string Name, string Root, bool CreateRoot = false);

public enum CreateProjectStatus
{
    Created,

    /// <summary>The root does not exist. Ask, then re-submit with CreateRoot set.</summary>
    NeedsRootConfirmation,

    Rejected,
}

public sealed record CreateProjectReply(
    CreateProjectStatus Status, Project? Project, string? Error, string? RootToCreate)
{
    public static CreateProjectReply Created(Project project) =>
        new(CreateProjectStatus.Created, project, null, null);

    public static CreateProjectReply Rejected(string error) =>
        new(CreateProjectStatus.Rejected, null, error, null);

    public static CreateProjectReply ConfirmRoot(string root) =>
        new(CreateProjectStatus.NeedsRootConfirmation, null, null, root);
}

/// <summary>
/// Validation lives here rather than in the dialog, which is what makes it
/// testable with no terminal. The view collects text, asks any question the
/// handler asks for, and shows whatever error comes back.
/// </summary>
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

        // Reject a path the filesystem cannot represent before offering to create
        // it — otherwise the confirmation prompt would be for something impossible.
        string full;
        try
        {
            full = Path.GetFullPath(root);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
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

        // Read back, so the caller gets exactly what was persisted rather than what
        // we hoped was persisted.
        return store.Load(sanitized) is { } saved
            ? CreateProjectReply.Created(saved)
            : CreateProjectReply.Rejected(
                $"'{sanitized}' was written but could not be read back.");
    }
}
