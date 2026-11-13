using Fleet.Shared.Keymap.Enums;

namespace Fleet.Tests.Docs;

// What the key tables are expected to say, kept by hand so that a table, the keymap and the keybind
// model cannot drift together unnoticed: a changed binding fails the test that names its key.
public static class KeyExpectations
{
    public static readonly IReadOnlyDictionary<string, FleetAction[]> DashboardKeys = new Dictionary<string, FleetAction[]>
    {
        ["j"] = [FleetAction.MoveDown],
        ["k"] = [FleetAction.MoveUp],
        ["g"] = [FleetAction.MoveFirst],
        ["G"] = [FleetAction.MoveLast],
        ["ctrl+d"] = [FleetAction.PageDown],
        ["ctrl+u"] = [FleetAction.PageUp],
        ["h"] = [FleetAction.PrevTab],
        ["l"] = [FleetAction.NextTab, FleetAction.OpenProject],
        ["n"] = [FleetAction.NewProject, FleetAction.NewAgent, FleetAction.AddRepository],
        ["m"] = [FleetAction.RemoveAgent, FleetAction.ManageRepository],
        ["e"] = [FleetAction.OpenEditor],
        ["p"] = [FleetAction.PullRepository],
        ["d"] = [FleetAction.RemoveRepository, FleetAction.RemoveProject],
        ["x"] = [FleetAction.ToggleHidden],
        ["w"] = [FleetAction.OpenWebApp],
        ["space"] = [FleetAction.OpenMenu],
        ["r"] = [FleetAction.Refresh],
        ["q"] = [FleetAction.Close],
        ["alt+o"] = [FleetAction.OpenHeadVoice],
    };

    public static readonly IReadOnlyDictionary<string, string> NotInTheKeymap = new Dictionary<string, string>
    {
        ["enter"] = "accepts in every list and dialog (Accepting), not a rebindable action",
        ["esc"] = "FleetKeys.Cancel: fixed, so a dialog can always be left",
        ["backspace"] = "FleetKeys.GoesBack: fixed, and a text field keeps it for deleting",
    };

    public static readonly IReadOnlyDictionary<string, string> PrefixActions = new Dictionary<string, string>
    {
        ["h"] = "focus-left",
        ["j"] = "focus-down",
        ["k"] = "focus-up",
        ["l"] = "focus-right",
        ["%"] = "split-right",
        ["\""] = "split-down",
        ["x"] = "kill-pane",
        ["z"] = "zoom",
        ["o"] = "next-pane",
        ["space"] = "menu",
        ["["] = "copy-mode",
        ["]"] = "paste",
        ["i"] = "paste-image",
        ["d"] = "detach",
        ["f f"] = "float-new",
        ["f t"] = "float-toggle",
        ["f e"] = "float-embed",
        ["f g"] = "float-mode",
        ["w w"] = "next-workspace",
        ["w s"] = "switch-project",
        ["Q"] = "menu quit",
        ["t c"] = "new-tab",
        ["t n"] = "next-tab",
        ["t p"] = "prev-tab",
        ["t 1"] = "tab 1",
        ["t 2"] = "tab 2",
        ["t 3"] = "tab 3",
        ["t 4"] = "tab 4",
        ["t 5"] = "tab 5",
        ["t 6"] = "tab 6",
        ["t 7"] = "tab 7",
        ["t 8"] = "tab 8",
        ["t 9"] = "tab 9",
        ["t x"] = "kill-tab",
        ["s p"] = "menu switch-project",
        ["s w"] = "menu save-session",
        ["s R"] = "menu remotes",
        ["s o"] = "menu open-port",
        ["s d"] = "detach",
        ["s q"] = "detach",
        ["s r"] = "reload",
        ["a d"] = "menu main-pane",
        ["a l"] = "menu list-agents",
        ["a e"] = "menu open-editor",
        ["a f"] = "menu browsefiles",
        ["a n"] = "menu notifications",
        ["a t"] = "menu todo-board",
        ["g k"] = "menu keybinds",
        ["g K"] = "menu show-menu-keys",
        ["g t"] = "menu button-hints",
        ["g T"] = "menu theme",
        ["g c v"] = "menu main-orchestrator-nvim",
        ["g c V"] = "menu sub-orchestrators-nvim",
        ["g c N"] = "menu nvim-config",
        ["g c E"] = "menu editor",
        ["g c O"] = "menu agent-harness",
        ["g c i"] = "menu auto-close",
        ["g c A"] = "menu aidlc-mode",
        ["g c C"] = "menu claude-profile",
        ["g c e"] = "menu edit-fleet-config",
        ["g c H"] = "menu head-model",
        ["g c M"] = "menu main-model",
        ["g c S"] = "menu sub-model",
        ["g c R"] = "menu agent-model",
        ["g c p"] = "menu settings",
        ["m b"] = "menu rebuild-dashboard",
        ["m x"] = "menu cleanup",
        ["m X"] = "menu hide-all",
        ["m l"] = "menu viewlogs",
        ["m u"] = "menu update",
        ["m v"] = "menu version",
        ["m W"] = "menu whats-new",

    };

    public static bool SameKey(string doc, string code) =>
        doc.Length == 1
            ? string.Equals(doc, code, StringComparison.Ordinal)
            : string.Equals(doc, code, StringComparison.OrdinalIgnoreCase);

    public static IReadOnlySet<FleetAction> ActionsDocumentedBy(IEnumerable<string> keys) =>
        keys.SelectMany(k => DashboardKeys.Where(d => SameKey(k, d.Key)).SelectMany(d => d.Value)).ToHashSet();
}
