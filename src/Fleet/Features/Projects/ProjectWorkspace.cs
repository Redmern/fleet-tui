using Fleet.Ports.Mux.Models;
using Fleet.Shared.Constants;

namespace Fleet.Features.Projects;

public static class ProjectWorkspace
{
    public static bool IsNative(Pane? dash, string projectName) =>
        dash is not null
        && string.Equals(dash.SessionName, projectName, StringComparison.OrdinalIgnoreCase);

    public static string HiddenWorkspaceFor(string projectName) =>
        $"{FleetWorkspaces.Hidden}-{projectName}";
}
