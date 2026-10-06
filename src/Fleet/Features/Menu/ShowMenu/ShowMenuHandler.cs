using Fleet.Features.Menu.ShowMenu.Models;
using Fleet.Shared.Keymap;
using Fleet.Shared.Keymap.Enums;
using Fleet.Ui;
using Fleet.Ui.Constants;
using Fleet.Ui.Models;

namespace Fleet.Features.Menu.ShowMenu;

public sealed class ShowMenuHandler(Keymap keymap)
{
    private const string More = "›";

    public IReadOnlyList<FleetMenuItem> Items(IReadOnlyList<FleetAction> actions) =>
        actions
            .Select(a => new FleetMenuItem(a, KeymapDefaults.Describe(a), keymap.DisplayFor(a)))
            .ToList();

    public IReadOnlyList<FleetMenuItem> Items(
        IReadOnlyList<MenuSection> sections, Func<FleetAction, string?> value) =>
        [
            .. sections
                .Where(s => s.Actions.Count > 0)
                .SelectMany(s => s.Actions.Select((a, i) => new FleetMenuItem(
                    a,
                    KeymapDefaults.Describe(a),
                    keymap.DisplayFor(a),
                    i == 0 ? s.Header : null,
                    value(a),
                    FleetMenus.OpensMore(a),
                    FleetMenus.IsToggle(a)))),
        ];

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
            ],
            Trailing(i))),
        ];
    }

    public static IReadOnlyDictionary<int, FleetRow> Headers(IReadOnlyList<FleetMenuItem> items) =>
        items
            .Select((item, index) => (item.Header, index))
            .Where(h => h.Header is not null)
            .ToDictionary(h => h.index, h => new FleetRow([FleetSpan.Muted($"── {h.Header} ──")]));

    public static int Width(IReadOnlyList<FleetRow> rows) =>
        rows.Count == 0 ? 0 : rows.Max(r => r.Text.Length + (r.Trailing is { Count: > 0 } ? 3 : 0)) + 2;

    public static int Height(IReadOnlyList<FleetRow> rows, int headers = 0) => Math.Max(1, rows.Count + headers);

    private static IReadOnlyList<FleetSpan>? Trailing(FleetMenuItem item) =>
        item.Value is { } value ? [FleetSpan.Muted(value)]
        : item.OpensMore ? [FleetSpan.Muted(More)]
        : null;
}
