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

        bool Prune(TabSnapshot tab)
        {
            var leaves = new List<LayoutSnapshot>();
            Leaves(tab.Root, leaves);

            var kept = new bool[leaves.Count];
            for (var i = 0; i < leaves.Count; i++)
            {
                kept[i] = !Unknown(leaves[i].Pane!);
                if (!kept[i])
                {
                    leaves[i].Pane = null;
                }
            }

            int KeptBefore(int index) => kept.Take(index).Count(k => k);

            if (tab.ActivePane >= 0 && tab.ActivePane < kept.Length)
            {
                tab.ActivePane = KeptBefore(tab.ActivePane);
            }

            if (tab.Zoomed >= 0 && tab.Zoomed < kept.Length)
            {
                tab.Zoomed = kept[tab.Zoomed] ? KeptBefore(tab.Zoomed) : -1;
            }

            return kept.Any(k => k);
        }

        foreach (var workspace in snapshot.Workspaces)
        {
            var emptiedBeforeActive = 0;
            for (var i = 0; i < workspace.Tabs.Count; i++)
            {
                if (!Prune(workspace.Tabs[i]) && i < workspace.ActiveTab)
                {
                    emptiedBeforeActive++;
                }
            }

            workspace.ActiveTab -= emptiedBeforeActive;
            workspace.Floats.RemoveAll(box => Unknown(box.Pane));
        }

        return dropped;
    }

    private static void Leaves(LayoutSnapshot? node, List<LayoutSnapshot> leaves)
    {
        if (node is null)
        {
            return;
        }

        if (node.Pane is not null)
        {
            leaves.Add(node);
            return;
        }

        Leaves(node.First, leaves);
        Leaves(node.Second, leaves);
    }
}
