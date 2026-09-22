using Fleet.Shared.Keymap;
using Fleet.Shared.Keymap.Enums;

namespace Fleet.Features.Menu.EditKeybinds;

public sealed record EditKeybindsRow(string Label, FleetAction? Action, bool IsHeader);

public static class EditKeybindsRows
{
    public static IReadOnlyList<EditKeybindsRow> Build()
    {
        var rows = new List<EditKeybindsRow> { new("Prefix", null, IsHeader: false) };

        foreach (var group in KeymapGroups.All)
        {
            rows.Add(new EditKeybindsRow(group.Label, null, IsHeader: true));
            rows.AddRange(group.Actions.Select(
                a => new EditKeybindsRow(KeymapDefaults.Describe(a), a, IsHeader: false)));
        }

        return rows;
    }
}
