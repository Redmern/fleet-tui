using Fleet.Features.Projects.CreateProject.Enums;
using Fleet.Ports.Projects.Models;

namespace Fleet.Features.Projects.CreateProject.Models;

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
