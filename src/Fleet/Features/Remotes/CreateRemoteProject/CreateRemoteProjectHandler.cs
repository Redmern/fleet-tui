using Fleet.Features.Remotes.CreateRemoteProject.Models;
using Fleet.Ports.Mux.Exceptions;
using Fleet.Ports.Remotes;
using Fleet.Ports.Remotes.Models;

namespace Fleet.Features.Remotes.CreateRemoteProject;

public sealed class CreateRemoteProjectHandler(IRemoteMachines remotes)
{
    public const string MissingDirectorySuffix = " does not exist; nothing was created.";

    private const string CreatedMark = " created";

    public async Task<RemoteProjectReply> HandleAsync(
        string host, string name, string path, bool createDirectory, CancellationToken ct = default)
    {
        name = name.Trim();
        path = path.Trim();

        if (name.Length == 0)
        {
            return RemoteProjectReply.Rejected("Name is required.");
        }

        if (path.Length == 0)
        {
            return RemoteProjectReply.Rejected("Path is required.");
        }

        try
        {
            var result = await remotes
                .CreateProjectAsync(host, new RemoteProjectRequest(name, path, createDirectory, Open: true), ct)
                .ConfigureAwait(false);

            if (!result.IsError)
            {
                return RemoteProjectReply.Created(CreatedName(result.Text, name));
            }

            return result.Text.EndsWith(MissingDirectorySuffix, StringComparison.Ordinal)
                ? RemoteProjectReply.ConfirmDirectory(result.Text[..^MissingDirectorySuffix.Length])
                : RemoteProjectReply.Rejected(result.Text);
        }
        catch (MuxUnavailableException e)
        {
            return RemoteProjectReply.Rejected(e.Message);
        }
    }

    private static string CreatedName(string text, string typed)
    {
        var at = text.IndexOf(CreatedMark, StringComparison.Ordinal);
        return at > 0 ? text[..at] : typed;
    }
}
