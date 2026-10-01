using Fleet.Ports.Mux.Enums;
using Fleet.Ports.Mux.Models;
using Fleet.Shared;

namespace Fleet.Ports.Mux;

public static class ProjectWindows
{
    public static string? For(IMuxDriver mux, IReadOnlyList<Pane> panes, string project, string projectRoot, bool preferCaller)
    {
        if (mux.Caps.HasFlag(MuxCaps.Workspaces))
        {
            return panes.FirstOrDefault(p => string.Equals(p.SessionName, project, StringComparison.OrdinalIgnoreCase))?.WindowId
                ?? project;
        }

        var caller = preferCaller ? panes.FirstOrDefault(p => p.Id == mux.CurrentPane)?.WindowId : null;
        return caller ?? panes.FirstOrDefault(p => PathKey.Same(p.Cwd, projectRoot))?.WindowId;
    }
}