using Fleet.Shared.Keymap.Enums;

namespace Fleet.Features.Menu.ShowMenu;

public static class ThisMachineSession
{
    private static readonly Dictionary<FleetAction, Func<IThisMachineScreens, Task>> Rows = new()
    {
        [FleetAction.SwitchProject] = s => s.SwitchProjectAsync(),
        [FleetAction.Notifications] = s => s.NotificationsAsync(),
        [FleetAction.Remotes] = s => s.RemotesAsync(),
        [FleetAction.SaveSession] = s => Done(s.SaveSession),
        [FleetAction.EditKeybinds] = s => Done(s.Keybinds),
        [FleetAction.EditTheme] = s => Done(s.Theme),
        [FleetAction.EditShowMenuKeys] = s => Done(() => s.Toggle(FleetAction.EditShowMenuKeys)),
        [FleetAction.EditButtonHints] = s => Done(() => s.Toggle(FleetAction.EditButtonHints)),
        [FleetAction.ViewLogs] = s => Done(s.Log),
        [FleetAction.UpdateFleet] = s => Done(s.Update),
        [FleetAction.ShowVersion] = s => Done(s.Version),
        [FleetAction.WhatsNew] = s => Done(s.WhatsNew),
    };

    public static IReadOnlyList<FleetAction> Screens => [.. Rows.Keys, FleetAction.ThisMachineSettings];

    public static async Task<int> RunAsync(FleetAction screen, IThisMachineScreens screens, Func<bool> wentBack)
    {
        var chosen = screen;
        var fromSettings = false;

        while (true)
        {
            if (chosen == FleetAction.ThisMachineSettings)
            {
                fromSettings = true;
                chosen = screens.ShowSettings(RemoteMenus.ThisMachineScreen);

                if (chosen == FleetAction.None)
                {
                    return 0;
                }
            }

            await Rows[chosen](screens).ConfigureAwait(false);

            if (!wentBack() || !fromSettings)
            {
                return 0;
            }

            chosen = FleetAction.ThisMachineSettings;
        }
    }

    private static Task Done(Action run)
    {
        run();
        return Task.CompletedTask;
    }
}
