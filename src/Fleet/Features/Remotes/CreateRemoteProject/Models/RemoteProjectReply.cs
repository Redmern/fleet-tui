using Fleet.Features.Remotes.CreateRemoteProject.Enums;

namespace Fleet.Features.Remotes.CreateRemoteProject.Models;

public sealed record RemoteProjectReply(
    RemoteProjectStatus Status, string? Project, string? Error, string? DirectoryToCreate)
{
    public static RemoteProjectReply Created(string project) =>
        new(RemoteProjectStatus.Created, project, null, null);

    public static RemoteProjectReply Rejected(string error) =>
        new(RemoteProjectStatus.Rejected, null, error, null);

    public static RemoteProjectReply ConfirmDirectory(string directory) =>
        new(RemoteProjectStatus.NeedsDirectory, null, null, directory);
}
