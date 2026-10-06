using Fleet.Ui.Constants;
using Fleet.Ui.Models;

namespace Fleet.Features.Menu.EditKeybinds;

public sealed record EditKeybindsBox(string Title, IReadOnlyList<EditKeybindsRow> Cells);

public sealed record EditKeybindsTab(string Title, IReadOnlyList<EditKeybindsBox> Boxes);

public readonly record struct EditKeybindsSpot(int Box, int Cell);

public sealed record EditKeybindsLayout(
    int Columns,
    int ColumnWidth,
    int KeyWidth,
    IReadOnlyList<int> ColumnOf,
    IReadOnlyList<int> TopOf)
{
    public int Left(int column) => column * (ColumnWidth + EditKeybindsGrid.Gap);

    public int LineOf(EditKeybindsSpot spot) => TopOf[spot.Box] + 2 + spot.Cell;
}

public sealed record EditKeybindsPicture(
    IReadOnlyList<IReadOnlyList<FleetSpan>> Lines,
    (int Line, int From, int To) Highlight);

public static class EditKeybindsGrid
{
    public const int Gap = 2;

    public const string PrefixTitle = "prefix";

    public const string MenusTab = "menus";

    public const string DashboardTab = "dashboard";

    public const string NavigationTab = "navigation";

    public const string OtherTab = "other";

    public const string ActionHeading = "action";

    public const string KeyHeading = "key";

    private const string MenuGroupStart = "fleet menu";

    private const string Separator = " │ ";

    private const int MaxColumns = 3;

    private const int Chrome = 4 + 3;

    private static readonly string[] MenuGroups = ["notifications"];

    private static readonly string[] DashboardGroups = ["dashboard", "project picker"];

    private static readonly string[] NavigationGroups = [PrefixTitle, "navigation", "anywhere, no prefix"];

    public static IReadOnlyList<EditKeybindsBox> Boxes(IReadOnlyList<EditKeybindsRow> rows)
    {
        var boxes = new List<EditKeybindsBox>();
        var title = PrefixTitle;
        var cells = new List<EditKeybindsRow>();

        void Close()
        {
            if (cells.Count > 0)
            {
                boxes.Add(new EditKeybindsBox(title, cells));
            }
        }

        foreach (var row in rows)
        {
            if (row.IsHeader)
            {
                Close();
                title = row.Label;
                cells = [];
            }
            else
            {
                cells.Add(row);
            }
        }

        Close();

        return boxes;
    }

    public static string TabFor(string group) =>
        group.StartsWith(MenuGroupStart, StringComparison.Ordinal) || MenuGroups.Contains(group) ? MenusTab
        : DashboardGroups.Contains(group) ? DashboardTab
        : NavigationGroups.Contains(group) ? NavigationTab
        : OtherTab;

    public static IReadOnlyList<EditKeybindsTab> Tabs(IReadOnlyList<EditKeybindsBox> boxes) =>
        [..
            new[] { MenusTab, DashboardTab, NavigationTab, OtherTab }
                .Select(tab => new EditKeybindsTab(tab, [.. boxes.Where(b => TabFor(b.Title) == tab)]))
                .Where(tab => tab.Boxes.Count > 0)];

    public static int Height(EditKeybindsBox box) => box.Cells.Count + 3;

    public static int NaturalWidth(IReadOnlyList<EditKeybindsBox> boxes, int keyWidth) =>
        boxes.Count == 0
            ? 0
            : boxes.Max(b => Math.Max(
                b.Title.Length + 5,
                Chrome + keyWidth + Math.Max(ActionHeading.Length, b.Cells.Max(c => c.Label.Length))));

    public static int Columns(int width, int naturalWidth, int boxCount)
    {
        for (var columns = Math.Min(MaxColumns, Math.Max(1, boxCount)); columns > 1; columns--)
        {
            if (columns * naturalWidth + (columns - 1) * Gap <= width)
            {
                return columns;
            }
        }

        return 1;
    }

    public static IReadOnlyList<int> Place(IReadOnlyList<int> heights, int columns)
    {
        if (heights.Count == 0)
        {
            return [];
        }

        for (var limit = heights.Max(); ; limit++)
        {
            var placed = Fill(heights, limit);

            if (placed[^1] < columns)
            {
                return placed;
            }
        }
    }

    private static int[] Fill(IReadOnlyList<int> heights, int limit)
    {
        var placed = new int[heights.Count];
        var column = 0;
        var used = 0;

        for (var i = 0; i < heights.Count; i++)
        {
            if (used > 0 && used + heights[i] > limit)
            {
                column++;
                used = 0;
            }

            placed[i] = column;
            used += heights[i];
        }

        return placed;
    }

    public static EditKeybindsLayout Layout(IReadOnlyList<EditKeybindsBox> boxes, int keyWidth, int width)
    {
        var natural = NaturalWidth(boxes, keyWidth);
        var columns = Columns(width, natural, boxes.Count);
        var columnOf = Place([.. boxes.Select(Height)], columns);
        var used = columnOf.Count == 0 ? 1 : columnOf[^1] + 1;
        var columnWidth = Math.Max(Chrome + keyWidth, (width - (used - 1) * Gap) / used);

        var topOf = new int[boxes.Count];
        var lines = new int[used];

        for (var i = 0; i < boxes.Count; i++)
        {
            topOf[i] = lines[columnOf[i]];
            lines[columnOf[i]] += Height(boxes[i]);
        }

        return new EditKeybindsLayout(used, columnWidth, keyWidth, columnOf, topOf);
    }

    public static EditKeybindsSpot Down(IReadOnlyList<EditKeybindsBox> boxes, EditKeybindsSpot spot) =>
        spot.Cell + 1 < boxes[spot.Box].Cells.Count
            ? spot with { Cell = spot.Cell + 1 }
            : new EditKeybindsSpot(Wrap(spot.Box + 1, boxes.Count), 0);

    public static EditKeybindsSpot Up(IReadOnlyList<EditKeybindsBox> boxes, EditKeybindsSpot spot)
    {
        if (spot.Cell > 0)
        {
            return spot with { Cell = spot.Cell - 1 };
        }

        var box = Wrap(spot.Box - 1, boxes.Count);

        return new EditKeybindsSpot(box, boxes[box].Cells.Count - 1);
    }

    public static EditKeybindsSpot First() => new(0, 0);

    public static EditKeybindsSpot Last(IReadOnlyList<EditKeybindsBox> boxes) =>
        new(boxes.Count - 1, boxes[^1].Cells.Count - 1);

    public static EditKeybindsSpot Clamp(IReadOnlyList<EditKeybindsBox> boxes, EditKeybindsSpot spot)
    {
        var box = Math.Clamp(spot.Box, 0, boxes.Count - 1);

        return new EditKeybindsSpot(box, Math.Clamp(spot.Cell, 0, boxes[box].Cells.Count - 1));
    }

    public static EditKeybindsPicture Draw(
        IReadOnlyList<EditKeybindsBox> boxes,
        EditKeybindsLayout layout,
        Func<EditKeybindsRow, string> keyOf,
        EditKeybindsSpot selected)
    {
        var height = Enumerable.Range(0, boxes.Count)
            .Select(i => layout.TopOf[i] + Height(boxes[i]))
            .DefaultIfEmpty(0)
            .Max();

        var lines = Enumerable.Range(0, height).Select(_ => new List<FleetSpan>()).ToList();

        for (var column = 0; column < layout.Columns; column++)
        {
            var segments = Enumerable.Range(0, height)
                .Select(_ => (IReadOnlyList<FleetSpan>)[FleetSpan.Plain(new string(' ', layout.ColumnWidth))])
                .ToArray();

            for (var b = 0; b < boxes.Count; b++)
            {
                if (layout.ColumnOf[b] != column)
                {
                    continue;
                }

                var top = layout.TopOf[b];

                segments[top] = TopEdge(boxes[b].Title, layout.ColumnWidth);
                segments[top + 1] = Heading(layout.KeyWidth, layout.ColumnWidth);

                for (var c = 0; c < boxes[b].Cells.Count; c++)
                {
                    var cell = boxes[b].Cells[c];
                    segments[top + 2 + c] = Cell(cell.Label, keyOf(cell), layout.KeyWidth, layout.ColumnWidth);
                }

                segments[top + Height(boxes[b]) - 1] = BottomEdge(layout.ColumnWidth);
            }

            for (var line = 0; line < height; line++)
            {
                if (column > 0)
                {
                    lines[line].Add(FleetSpan.Plain(new string(' ', Gap)));
                }

                lines[line].AddRange(segments[line]);
            }
        }

        var left = boxes.Count == 0 ? 0 : layout.Left(layout.ColumnOf[selected.Box]);

        return new EditKeybindsPicture(
            lines,
            boxes.Count == 0 ? (-1, 0, 0) : (layout.LineOf(selected), left + 1, left + layout.ColumnWidth - 1));
    }

    public static int KeyWidth(IReadOnlyList<EditKeybindsBox> boxes, Func<EditKeybindsRow, string> keyOf) =>
        Math.Max(KeyHeading.Length, boxes.SelectMany(b => b.Cells).Select(c => keyOf(c).Length).DefaultIfEmpty(0).Max());

    private static IReadOnlyList<FleetSpan> TopEdge(string title, int width)
    {
        var room = Math.Max(0, width - 5);
        var shown = Fit(title, room);

        return
        [
            new FleetSpan("╭ ", FleetTones.Edge),
            new FleetSpan(shown, FleetTones.Title),
            new FleetSpan(" " + new string('─', Math.Max(0, width - 4 - shown.Length)) + "╮", FleetTones.Edge),
        ];
    }

    private static IReadOnlyList<FleetSpan> BottomEdge(int width) =>
        [new FleetSpan("╰" + new string('─', Math.Max(0, width - 2)) + "╯", FleetTones.Edge)];

    private static IReadOnlyList<FleetSpan> Heading(int keyWidth, int width) =>
        Row(FleetSpan.Muted(ActionHeading), FleetSpan.Muted(KeyHeading), keyWidth, width);

    private static IReadOnlyList<FleetSpan> Cell(string label, string key, int keyWidth, int width) =>
        Row(FleetSpan.Plain(label), new FleetSpan(key, FleetTones.Key), keyWidth, width);

    private static IReadOnlyList<FleetSpan> Row(FleetSpan label, FleetSpan key, int keyWidth, int width)
    {
        var room = Math.Max(0, width - Chrome - keyWidth);

        return
        [
            new FleetSpan("│", FleetTones.Edge),
            FleetSpan.Plain(" "),
            label with { Text = Fit(label.Text, room).PadRight(room) },
            new FleetSpan(Separator, FleetTones.Edge),
            key with { Text = Fit(key.Text, keyWidth).PadRight(keyWidth) },
            FleetSpan.Plain(" "),
            new FleetSpan("│", FleetTones.Edge),
        ];
    }

    private static string Fit(string text, int room) =>
        text.Length <= room ? text : room <= 0 ? string.Empty : text[..(room - 1)] + "…";

    private static int Wrap(int index, int count) => count <= 0 ? 0 : ((index % count) + count) % count;
}
