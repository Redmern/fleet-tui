using System.Text.RegularExpressions;
using Fleet.Features.Menu.ShowMenu;
using Fleet.Shared.Keybinds;
using Fleet.Shared.Keybinds.Enums;
using Fleet.Shared.Keymap;
using Fleet.Shared.Keymap.Enums;
using Fleet.Tests.Docs;
using Fleet.Ui.Constants;

namespace Fleet.Tests.Shared;

// Adding a FleetAction means touching a fixed list of places. This test walks every action
// through every place and names the action and the place that is missing. An action that is
// intentionally absent from a place is listed in Exemptions with the reason.
//
// The id place (FleetActionIds) is not walked here: FleetActionIdsTests.Every_action_round_trips
// already proves every action has an id that parses back to it, and the fallback arm of
// FleetActionIds.For gives an action without an explicit arm its lowercased name.
//
// Handler means a "case FleetAction.X" in the dashboard (ShowDashboardView.Dispatch), the fleet menu
// (MenuCommand), or the project picker (PickProjectView). Menu is only required of an action
// in a "fleet menu" group of KeymapGroups. Docs means the README keys table names its key, or
// its prefix chord is in the README and DESIGN prefix tables.
public class FleetActionPlacesTests
{
    public enum Place
    {
        DefaultKey,
        Group,
        Icon,
        KeybindsJson,
        Handler,
        Menu,
        Docs,
    }

    private static readonly List<string> DuplicateExemptions = [];

    private static readonly IReadOnlyDictionary<(FleetAction, Place), string> Exemptions = Build();

    private static IReadOnlyDictionary<(FleetAction, Place), string> Build()
    {
        var all = new Dictionary<(FleetAction, Place), string>();

        void Exempt(Place place, string reason, params FleetAction[] actions)
        {
            foreach (var action in actions)
            {
                if (!all.TryAdd((action, place), reason))
                {
                    DuplicateExemptions.Add($"FleetAction.{action} / {place} is exempted twice");
                }
            }
        }

        Exempt(Place.Icon, "a list motion, never drawn as an icon row or button", FleetAction.MoveDown, FleetAction.MoveUp, FleetAction.MoveFirst, FleetAction.MoveLast, FleetAction.PageDown, FleetAction.PageUp);
        Exempt(Place.Icon, "switches the tab bar; there is no icon row for it", FleetAction.PrevTab, FleetAction.NextTab);
        Exempt(Place.Icon, "the dashboard opens the menu from its menu button and the space key; there is no icon row for it", FleetAction.OpenMenu);
        Exempt(Place.Handler, "FleetKeys.ApplyMotions moves every list, there is no per-view case", FleetAction.MoveDown, FleetAction.MoveUp, FleetAction.MoveFirst, FleetAction.MoveLast, FleetAction.PageDown, FleetAction.PageUp);
        Exempt(Place.Icon, "shown as a text hint in the repository action bar, not as an icon row", FleetAction.PullRepository, FleetAction.ManageRepository);
        Exempt(Place.Icon, "shown as a text hint in the notifications action bar, not as an icon row", FleetAction.DismissNotice, FleetAction.DismissAllNotices);
        Exempt(Place.Docs, "described in the README prose under '## The menu' (d dismisses, D dismisses all shown); the keys table lists dashboard-wide keys", FleetAction.DismissNotice, FleetAction.DismissAllNotices);
        Exempt(Place.DefaultKey, "no key: ChangeHarness is reached from the dashboard menu item, the configure-agent card and the MCP change_harness tool", FleetAction.ChangeHarness);
        Exempt(Place.DefaultKey, "no key: StopAgent is reached from the configure-agent card and the MCP stop_agent tool", FleetAction.StopAgent);
        Exempt(Place.Group, "no key: ChangeHarness is reached from the dashboard menu item, the configure-agent card and the MCP change_harness tool", FleetAction.ChangeHarness);
        Exempt(Place.Group, "no key: StopAgent is reached from the configure-agent card and the MCP stop_agent tool", FleetAction.StopAgent);
        Exempt(Place.KeybindsJson, "no key: ChangeHarness is reached from the dashboard menu item, the configure-agent card and the MCP change_harness tool", FleetAction.ChangeHarness);
        Exempt(Place.KeybindsJson, "no key: StopAgent is reached from the configure-agent card and the MCP stop_agent tool", FleetAction.StopAgent);
        Exempt(Place.Handler, "GAP, not by design: the dashboard menu offers it (DashboardWiring.MenuActions) and the head can request it (DashboardActions.Served), but ShowDashboardView.Dispatch has no case and no default, so choosing it does nothing; the working path is the 'o' choice of the configure-agent card (ChooseHarness)", FleetAction.ChangeHarness);
        Exempt(Place.Handler, "never offered in the dashboard menu or served to the head; stopping runs from the configure-agent card (stopper) and the MCP stop_agent tool", FleetAction.StopAgent);
        Exempt(Place.Docs, "no key: ChangeHarness is reached from the dashboard menu item, the configure-agent card and the MCP change_harness tool", FleetAction.ChangeHarness);
        Exempt(Place.Docs, "no key: StopAgent is reached from the configure-agent card and the MCP stop_agent tool", FleetAction.StopAgent);
        Exempt(Place.Icon, "global direct key (alt+o) of the multiplexer, never a menu row", FleetAction.OpenHeadVoice);
        Exempt(Place.Handler, "global direct key (alt+o) handled by the multiplexer client", FleetAction.OpenHeadVoice);
        Exempt(Place.Icon, "handled by FleetKeyHints on every menu, never drawn as a row", FleetAction.RevealMenuKeys, FleetAction.HoldMenuKeys);
        Exempt(Place.Handler, "handled by FleetKeyHints on every menu", FleetAction.RevealMenuKeys, FleetAction.HoldMenuKeys);
        Exempt(Place.Docs, "described in the README prose under '## The menu' (hold / to show keys, ? to reveal them)", FleetAction.RevealMenuKeys, FleetAction.HoldMenuKeys);
        Exempt(Place.Handler, "a submenu: FleetMenus.IsSubmenu opens it, there is no case", FleetAction.OpenSettings, FleetAction.OpenFleetConfigMenu);
        Exempt(Place.Docs, "a submenu: the group tables name its group (g, g c), not the submenu action", FleetAction.OpenSettings, FleetAction.OpenFleetConfigMenu);
        Exempt(Place.Handler, "a menu toggle: MenuCommand.RunsHeadless flips it through Adapters.Toggles, there is no case", FleetAction.EditMainOrchestratorInNvim, FleetAction.EditSubOrchestratorsInNvim, FleetAction.EditNvimConfig, FleetAction.EditEditor, FleetAction.EditDefaultHarness, FleetAction.EditShowMenuKeys, FleetAction.EditButtonHints);
        return all;
    }

    private static string Source(string path) => File.ReadAllText(RepoFiles.Path("src", "Fleet", path));

    private static bool Cased(string source, FleetAction action) =>
        Regex.IsMatch(source, $@"case FleetAction\.{action}\b");

    private static IReadOnlyList<string> JsonActions { get; } =
    [
        .. KeybindDefaults.Set.Entries.Where(e => e.Targets.ContainsKey(KeybindTarget.Mux)).Select(e => e.Action),
    ];

    private static bool MenuEligible(FleetAction action) =>
        KeymapGroups.All.Any(g => g.Label.StartsWith("fleet menu", StringComparison.Ordinal) && g.Actions.Contains(action));

    private static bool Has(FleetAction action, Place place)
    {
        var id = FleetActionIds.For(action);

        return place switch
        {
            Place.DefaultKey => KeymapDefaults.Bindings.ContainsKey(action),
            Place.Group => KeymapGroups.All.Any(g => g.Actions.Contains(action)),
            Place.Icon => FleetIcons.For(action) is not null,
            Place.KeybindsJson => KeybindDefaults.Set.Find(KeybindLegacy.FleetUiId(action)) is not null
                || JsonActions.Contains("menu " + id),
            Place.Handler => Cased(Source("Features/Dashboard/ShowDashboard/ShowDashboardView.cs"), action)
                || Cased(Source("Cli/Commands/MenuCommand.cs"), action)
                || Cased(Source("Features/Projects/PickProject/PickProjectView.cs"), action),
            Place.Menu => FleetMenus.Actions([.. FleetMenus.Main, .. FleetMenus.Settings, .. FleetMenus.FleetConfig]).Contains(action),
            Place.Docs => KeyExpectations.ActionsDocumentedBy(DocTables.ReadmeKeys).Contains(action)
                || (JsonActions.Contains("menu " + id) && Documented(id)),
            _ => false,
        };
    }

    private static bool Documented(string id)
    {
        var chords = KeybindDefaults.Set.Entries.Where(e => e.Action == "menu " + id).Select(e => e.Chord).ToList();

        return chords.Count > 0 && chords.All(c => DocTables.ReadmePrefixChords.Contains(c) && DocTables.DesignPrefixChords.Contains(c));
    }

    private static bool Applies(FleetAction action, Place place) => place != Place.Menu || MenuEligible(action);

    public static IEnumerable<FleetAction> Actions => Enum.GetValues<FleetAction>().Where(a => a != FleetAction.None);

    [Fact]
    public void Every_action_is_in_every_place_or_exempt_with_a_reason()
    {
        var missing = (
            from action in Actions
            from place in Enum.GetValues<Place>()
            where Applies(action, place) && !Has(action, place) && !Exemptions.ContainsKey((action, place))
            select $"FleetAction.{action} is missing from {place}").ToList();

        Assert.True(missing.Count == 0, string.Join('\n', missing));
    }

    [Fact]
    public void The_menu_place_is_not_vacuous()
    {
        Assert.Contains(Actions, MenuEligible);
    }

    private static readonly FleetAction[] KnownUndispatched = [FleetAction.ChangeHarness];

    [Fact]
    public void Every_action_the_dashboard_offers_has_a_case_in_dispatch_except_the_known_gap()
    {
        var wiring = Source("Cli/Composition/DashboardWiring.cs");
        var menu = Regex.Match(wiring, @"MenuActions =\s*\[(.*?)\];", RegexOptions.Singleline).Groups[1].Value;
        var dispatch = Source("Features/Dashboard/ShowDashboard/ShowDashboardView.cs");

        var undispatched = DashboardActions.Served
            .Concat(Actions.Where(a => menu.Contains($"FleetAction.{a},", StringComparison.Ordinal)))
            .Distinct()
            .Where(a => !Cased(dispatch, a))
            .ToList();

        var problems = undispatched.Except(KnownUndispatched).Select(a => $"FleetAction.{a} is offered by the dashboard but ShowDashboardView.Dispatch has no case for it")
            .Concat(KnownUndispatched.Except(undispatched).Select(a => $"FleetAction.{a} is dispatched now; drop it from KnownUndispatched and its Handler exemption"))
            .ToList();

        Assert.True(problems.Count == 0, string.Join('\n', problems));
    }

    [Fact]
    public void An_exemption_is_removed_once_its_place_exists()
    {
        var stale = Exemptions.Keys
            .Where(k => Has(k.Item1, k.Item2))
            .Select(k => $"FleetAction.{k.Item1} is now in {k.Item2}; drop its exemption")
            .ToList();

        Assert.True(stale.Count == 0, string.Join('\n', stale));
    }

    [Fact]
    public void An_exemption_is_listed_once_and_gives_a_reason()
    {
        var blank = Exemptions.Where(e => string.IsNullOrWhiteSpace(e.Value)).Select(e => $"FleetAction.{e.Key.Item1} / {e.Key.Item2} has no reason");

        var problems = DuplicateExemptions.Concat(blank).ToList();

        Assert.True(problems.Count == 0, string.Join('\n', problems));
    }
}
