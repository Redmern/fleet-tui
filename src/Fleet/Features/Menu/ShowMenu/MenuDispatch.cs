using Fleet.Shared.Keymap;
using Fleet.Shared.Keymap.Enums;
using Fleet.Shared.Viewing;
using Fleet.Shared.Viewing.Enums;
using Fleet.Shared.Viewing.Models;

namespace Fleet.Features.Menu.ShowMenu;

public sealed class MenuDispatch(Func<ViewedFrom?> viewer, Func<string, bool> handBack)
{
    public ScreenRoute Settle(FleetAction action)
    {
        var id = FleetActionIds.For(action);

        if (viewer() is not { } drawnFor)
        {
            return ViewerScreens.Legacy.Contains(id, StringComparer.Ordinal) && handBack(id) ? ScreenRoute.HandBack : ScreenRoute.Here;
        }

        return ViewerScreens.Route(drawnFor, action) switch
        {
            ScreenRoute.HandBack => handBack(id) ? ScreenRoute.HandBack : ScreenRoute.Hint,
            var route => route,
        };
    }

    public async Task<int> StartAsync(FleetAction requested, Action headless, Action<string> hint, Func<Task<int>> menu)
    {
        if (requested == FleetAction.None)
        {
            return await menu().ConfigureAwait(false);
        }

        switch (Settle(requested))
        {
            case ScreenRoute.HandBack:
                return 0;
            case ScreenRoute.Hint:
                hint(ViewerHints.ScreensWhereYouSit);
                return 0;
        }

        if (FleetMenus.IsToggle(requested))
        {
            headless();
            return 0;
        }

        return await menu().ConfigureAwait(false);
    }

    public async Task<(ScreenRoute Route, int Code)> RunAsync(FleetAction action, Func<FleetAction, Task<int>> here, Action<string> hint)
    {
        var route = Settle(action);

        if (route == ScreenRoute.Here)
        {
            return (route, await here(action).ConfigureAwait(false));
        }

        if (route == ScreenRoute.Hint)
        {
            hint(ViewerHints.ScreensWhereYouSit);
        }

        return (route, 0);
    }
}
