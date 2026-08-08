using Fleet.Ports.Requests;
using Fleet.Shared;
using Fleet.Shared.Keymap;
using Fleet.Shared.Keymap.Enums;

namespace Fleet.Platform.Storage;

public sealed class FileActionRequestStore : IActionRequestStore
{
    public void Submit(string project, FleetAction action)
    {
        var file = FileFor(project);

        if (file is null || action == FleetAction.None)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(FleetPaths.Requests);
            File.WriteAllText(file, FleetActionIds.For(action));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    public FleetAction TakePending(string project)
    {
        var file = FileFor(project);

        if (file is null || !File.Exists(file))
        {
            return FleetAction.None;
        }

        try
        {
            var id = File.ReadAllText(file);
            File.Delete(file);

            return FleetActionIds.Parse(id);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return FleetAction.None;
        }
    }

    private static string? FileFor(string project)
    {
        var name = ProjectName.Sanitize(project);

        return name.Length == 0
            ? null
            : Path.Combine(FleetPaths.Requests, name + ".request");
    }
}
