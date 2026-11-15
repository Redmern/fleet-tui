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

    public async Task<(ScreenRoute Route, int Code)> RunAsync(FleetAction action, Func<FleetAction, Task<int>> here, Action hint)
    {
        var route = Settle(action);

        if (route == ScreenRoute.Here)
        {
            return (route, await here(action).ConfigureAwait(false));
        }

        if (route == ScreenRoute.Hint)
        {
            hint();
        }

        return (route, 0);
    }
}
