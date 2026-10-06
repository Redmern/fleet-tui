using Fleet.Ports.Mux.Enums;
using Fleet.Ports.Mux.Models;
using Fleet.Shared.Constants;

namespace Fleet.Ports.Mux;

public static class HiddenSpawn
{
    public static string Workspace(IMuxDriver mux, string project) =>
        mux.Caps.HasFlag(MuxCaps.Workspaces) ? FleetWorkspaces.HiddenFor(project) : FleetWorkspaces.Hidden;

    public static SpawnOptions Into(IMuxDriver mux, string project, SpawnOptions options)
    {
        var workspace = Workspace(mux, project);

        return options with
        {
            SessionName = workspace,
            Workspace = workspace,
            WindowId = null,
            NewWindow = true,
        };
    }
}
