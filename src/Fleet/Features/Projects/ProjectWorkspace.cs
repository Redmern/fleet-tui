using Fleet.Ports.Mux.Models;

namespace Fleet.Features.Projects;

public static class ProjectWorkspace
{
    public static bool IsNative(Pane? dash, string projectName) =>
        dash is not null
        && string.Equals(dash.SessionName, projectName, StringComparison.OrdinalIgnoreCase);
}
