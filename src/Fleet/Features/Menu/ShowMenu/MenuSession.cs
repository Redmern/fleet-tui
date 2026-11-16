using Fleet.Features.Menu.ShowMenu.Models;
using Fleet.Shared.Keymap;
using Fleet.Shared.Keymap.Enums;
using Fleet.Shared.Viewing.Enums;
using Fleet.Shared.Viewing.Models;

namespace Fleet.Features.Menu.ShowMenu;

public sealed class MenuSession(
    Func<ViewedFrom?> viewer,
    MenuDispatch dispatch,
    Func<MenuScreen, Task<FleetAction>> show,
    Func<Task<bool>> canOpenEditor,
    Func<FleetAction, Task<int>> perform,
    Func<bool> wentBack,
    Action<string> hint)
{
    private FleetAction _switchedTo = FleetAction.None;

    private bool _regroup;

    public Action? Opened(string? action, string? drawnFor, string? openedBy)
    {
        var switchTo = action is null ? FleetAction.None : FleetActionIds.Parse(action);
        var regroup = !string.Equals(drawnFor, openedBy, StringComparison.Ordinal);

        if (switchTo == FleetAction.None && !regroup)
        {
            return null;
        }

        return () =>
        {
            if (switchTo != FleetAction.None)
            {
                _switchedTo = switchTo;
            }

            _regroup = regroup;
        };
    }

    public async Task<MenuEnd> RunAsync(FleetAction requested)
    {
        var chosen = requested;
        var settledAtStart = requested != FleetAction.None && !FleetMenus.IsSubmenu(requested);
        FleetAction? pickedFrom = null;

        while (true)
        {
            if (chosen == FleetAction.None || FleetMenus.IsSubmenu(chosen))
            {
                var submenu = chosen;
                var editor = submenu != FleetAction.None || await canOpenEditor().ConfigureAwait(false);

                chosen = await show(RemoteMenus.Screen(viewer(), submenu, editor)).ConfigureAwait(false);
                pickedFrom = submenu;

                if (_regroup && chosen == FleetAction.None && submenu == FleetAction.None && _switchedTo == FleetAction.None)
                {
                    _regroup = false;
                    continue;
                }

                if (chosen == FleetAction.None && submenu != FleetAction.None && wentBack())
                {
                    chosen = FleetMenus.Parent(submenu);
                    continue;
                }

                if (chosen == FleetAction.None && submenu == FleetAction.None && _switchedTo != FleetAction.None)
                {
                    (chosen, _switchedTo) = (_switchedTo, FleetAction.None);
                    pickedFrom = null;
                    _regroup = false;

                    if (chosen is FleetAction.OpenProject or FleetAction.NewProject)
                    {
                        return new MenuEnd(0, chosen);
                    }

                    continue;
                }

                if (chosen == FleetAction.None)
                {
                    return new MenuEnd(0);
                }

                continue;
            }

            var (route, code) = settledAtStart
                ? (ScreenRoute.Here, await perform(chosen).ConfigureAwait(false))
                : await dispatch.RunAsync(chosen, perform, hint).ConfigureAwait(false);
            settledAtStart = false;

            if (route == ScreenRoute.Hint && pickedFrom is { } shownBefore)
            {
                chosen = shownBefore;
                continue;
            }

            if (route != ScreenRoute.Here || code != 0 || !wentBack())
            {
                return new MenuEnd(code);
            }

            chosen = FleetMenus.Parent(chosen);
        }
    }
}
