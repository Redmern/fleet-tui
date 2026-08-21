using Fleet.Features.Menu.ShowMenu.Models;
using Fleet.Shared.Keymap;
using Fleet.Shared.Keymap.Enums;
using Fleet.Ui;
using Fleet.Ui.Constants;
using Fleet.Ui.Models;

namespace Fleet.Features.Menu.ShowMenu;

public sealed class ShowMenuHandler(Keymap keymap)
{
    public IReadOnlyList<FleetMenuItem> Items(IReadOnlyList<FleetAction> actions) =>
        actions
            .Select(a => new FleetMenuItem(a, KeymapDefaults.Describe(a), keymap.DisplayFor(a)))
            .ToList();

    public static IReadOnlyList<FleetRow> Rows(IReadOnlyList<FleetMenuItem> items)
    {
        if (items.Count == 0)
        {
            return [];
        }

        var keyWidth = items.Max(i => i.KeyText.Length);

        return
        [
            .. items.Select(i => new FleetRow(
            [
                new FleetSpan($"{i.KeyText.PadRight(keyWidth)}   ", FleetTones.Key),
                FleetSpan.Plain(i.Label),
            ])),
        ];
    }

    public static int Width(IReadOnlyList<FleetRow> rows) =>
        rows.Count == 0 ? 0 : rows.Max(r => r.Text.Length) + 2;

    public static int Height(IReadOnlyList<FleetRow> rows) => Math.Max(1, rows.Count);
}
