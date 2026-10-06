using Fleet.Features.Menu.ShowMenu.Models;
using Fleet.Shared.Keymap.Enums;
using Fleet.Ui;
using Fleet.Ui.Constants;
using Fleet.Ui.Models;

namespace Fleet.Features.Menu.ShowMenu;

public sealed class ShowMenuHandler(Keymap keymap)
{
    private const string More = "›";

    private const string KeyGap = "   ";

    private const string IconGap = "  ";

    public const int Padding = 2;

    public IReadOnlyList<FleetMenuItem> Items(IReadOnlyList<FleetAction> actions) =>
        actions
            .Select(a => new FleetMenuItem(a, FleetMenus.Label(a), keymap.DisplayFor(a)))
            .ToList();

    public IReadOnlyList<FleetMenuItem> Items(
        IReadOnlyList<MenuSection> sections, Func<FleetAction, string?> value) =>
        [
            .. sections
                .Where(s => s.Actions.Count > 0)
                .SelectMany(s => s.Actions.Select((a, i) => new FleetMenuItem(
                    a,
                    FleetMenus.Label(a),
                    keymap.DisplayFor(a),
                    i == 0 ? s.Header : null,
                    value(a),
                    FleetMenus.OpensMore(a),
                    FleetMenus.IsToggle(a),
                    i == 0 ? s.Icon : null))),
        ];

    public static IReadOnlyList<FleetRow> Rows(IReadOnlyList<FleetMenuItem> items, bool keys = true)
    {
        if (items.Count == 0)
        {
            return [];
        }

        var keyWidth = items.Max(i => i.KeyText.Length);
        var icons = items.Any(i => FleetIcons.For(i.Action) is not null);

        return
        [
            .. items.Select(i => new FleetRow(
            [
                .. keys ? [new FleetSpan($"{i.KeyText.PadRight(keyWidth)}{KeyGap}", FleetTones.Key)] : Array.Empty<FleetSpan>(),
                .. icons ? [FleetSpan.Plain($"{FleetIcons.For(i.Action) ?? " "}{IconGap}")] : Array.Empty<FleetSpan>(),
                FleetSpan.Plain(i.Label),
            ],
            Trailing(i))),
        ];
    }

    public static IReadOnlyDictionary<int, FleetRow> Headers(IReadOnlyList<FleetMenuItem> items, bool keys = true)
    {
        var indent = keys && items.Count > 0 ? items.Max(i => i.KeyText.Length) + KeyGap.Length : 0;

        return items
            .Select((item, index) => (item.Header, item.HeaderIcon, index))
            .Where(h => h.Header is not null)
            .ToDictionary(
                h => h.index,
                h => new FleetRow([FleetSpan.Muted(
                    $"{new string(' ', indent)}{(h.HeaderIcon is null ? string.Empty : h.HeaderIcon + IconGap)}{h.Header}")]));
    }

    public static IReadOnlyList<int> Gaps(IReadOnlyList<FleetMenuItem> items) =>
        [.. items.Select((item, index) => (item.Header, index)).Where(h => h.Header is not null && h.index > 0).Select(h => h.index - 1)];

    public static int Width(IReadOnlyList<FleetRow> rows) =>
        rows.Count == 0 ? 0 : rows.Max(r => r.Text.Length + (r.Trailing is { Count: > 0 } ? 3 : 0)) + 2;

    public static int Height(IReadOnlyList<FleetRow> rows, int headers = 0, int gaps = 0) =>
        Math.Max(1, rows.Count + headers + gaps);

    private static IReadOnlyList<FleetSpan>? Trailing(FleetMenuItem item) =>
        item.Value is { } value ? [FleetSpan.Muted(value)]
        : item.OpensMore ? [FleetSpan.Muted(More)]
        : null;
}
