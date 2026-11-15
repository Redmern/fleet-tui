using Fleet.Features.Menu.ShowMenu.Models;
using Fleet.Shared.Keymap.Enums;
using Fleet.Shared.Viewing;
using Fleet.Shared.Viewing.Enums;
using Fleet.Shared.Viewing.Models;

namespace Fleet.Features.Menu.ShowMenu;

public static class RemoteMenus
{
    public const string ThisMachineTitle = "FLEET this machine";

    public const string UnnamedMachine = "the remote machine";

    private static readonly IReadOnlyList<FleetAction> FleetRowsAddedToMain =
        [FleetAction.Remotes, FleetAction.SaveSession, FleetAction.ThisMachineSettings];

    public static IReadOnlyList<MenuSection> ThisMachine { get; } =
    [
        new(null,
        [
            FleetAction.SaveSession,
            FleetAction.Remotes,
            FleetAction.EditKeybinds,
            FleetAction.EditShowMenuKeys,
            FleetAction.EditButtonHints,
            FleetAction.EditTheme,
            FleetAction.ViewLogs,
            FleetAction.UpdateFleet,
            FleetAction.ShowVersion,
            FleetAction.WhatsNew,
        ]),
    ];

    public static string ProjectTitle(string machine) => $"PROJECT on {machine}";

    public static string MachineTitle(string machine) => $"MACHINE {machine}";

    public static ViewedFrom Unnamed(ViewedFrom viewer) => viewer with { Name = UnnamedMachine };

    public static IReadOnlyList<MenuSection> For(ViewedFrom? viewer, FleetAction submenu)
    {
        var local = FleetMenus.For(submenu);

        if (viewer is null || !Regroups(submenu))
        {
            return local;
        }

        var rows = FleetMenus.Actions(local);
        var fleet = viewer.Kind == ViewKind.Ssh || submenu != FleetAction.None
            ? []
            : rows.Where(a => ActionGroups.Of(a) == ActionGroup.ThisMachine).Concat(FleetRowsAddedToMain).ToList();

        return
        [
            new(ProjectTitle(viewer.Name), [.. rows.Where(a => ActionGroups.Of(a) == ActionGroup.Project)]),
            new(MachineTitle(viewer.Name), [.. rows.Where(a => ActionGroups.Of(a) == ActionGroup.Machine)]),
            .. fleet.Count == 0 ? Array.Empty<MenuSection>() : [new MenuSection(ThisMachineTitle, fleet)],
        ];
    }

    public static string? Note(ViewedFrom? viewer, FleetAction submenu) =>
        viewer is { Kind: ViewKind.Ssh } && Regroups(submenu) ? ViewerHints.ScreensWhereYouSit : null;

    private static bool Regroups(FleetAction submenu) => submenu is FleetAction.None or FleetAction.OpenSettings;
}
