using Fleet.Ports.Requests;

namespace Fleet.Platform.Storage;

public sealed class FileWorkspaceRequestStore : IWorkspaceRequestStore
{
    public static string File => Path.Combine(FleetPaths.Requests, "workspace.request");

    public void Submit(string workspace)
    {
        if (string.IsNullOrWhiteSpace(workspace))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(FleetPaths.Requests);
            System.IO.File.WriteAllText(File, workspace);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }
}
