using Fleet.Features.Menu.ShowMenu.Models;
using Fleet.Shared.Keymap;
using Fleet.Shared.Keymap.Enums;
using Fleet.Ui;

namespace Fleet.Features.Menu.ShowMenu;

public sealed class ShowMenuHandler(Keymap keymap)
{
    public IReadOnlyList<FleetMenuItem> Items(IReadOnlyList<FleetAction> actions) =>
        actions
            .Select(a => new FleetMenuItem(a, KeymapDefaults.Describe(a), keymap.TextFor(a)))
            .ToList();

    public static IReadOnlyList<string> Rows(IReadOnlyList<FleetMenuItem> items)
    {
        if (items.Count == 0)
        {
            return [];
        }

        var width = items.Max(i => i.Label.Length);

        return items
            .Select(i => $"{i.Label.PadRight(width)}   {i.KeyText}")
            .ToList();
    }
}
