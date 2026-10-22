using Fleet.Platform.Mux.Embedded.Model;
using Fleet.Shared.Orchestrations;

namespace Fleet.Platform.Mux.Embedded.Daemon;

public static class SavedSubOrchestrators
{
    public static bool IsSubOrchestrator(string cwd)
    {
        var parts = cwd.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);

        return parts.Length >= 3
            && string.Equals(parts[^3], OrchestrationPaths.Folder, StringComparison.OrdinalIgnoreCase)
            && string.Equals(parts[^2], OrchestrationPaths.Orchestrations, StringComparison.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<string> DropUnknown(SessionSnapshot snapshot, Func<string, bool> isKnownAgent)
    {
        var dropped = new List<string>();

        bool Unknown(PaneSnapshot pane)
        {
            if (!IsSubOrchestrator(pane.Cwd) || isKnownAgent(pane.Cwd))
            {
                return false;
            }

            dropped.Add(pane.Cwd);
            return true;
        }

        void Prune(LayoutSnapshot? node)
        {
            if (node is null)
            {
                return;
            }

            if (node.Pane is { } pane && Unknown(pane))
            {
                node.Pane = null;
            }

            Prune(node.First);
            Prune(node.Second);
        }

        foreach (var workspace in snapshot.Workspaces)
        {
            foreach (var tab in workspace.Tabs)
            {
                Prune(tab.Root);
            }

            workspace.Floats.RemoveAll(box => Unknown(box.Pane));
        }

        return dropped;
    }
}
