using System.Collections;
using System.Collections.Specialized;
using System.Text;
using Fleet.Ui.Constants;
using Fleet.Ui.Models;
using Terminal.Gui.Drawing;
using Terminal.Gui.Views;

using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Fleet.Ui;

public sealed class FleetRowSource : IListDataSource
{
    private static readonly Rune Blank = (Rune)' ';

    private static readonly Rune CapLeft = FleetGlyphs.PillLeft.EnumerateRunes().First();

    private static readonly Rune CapRight = FleetGlyphs.PillRight.EnumerateRunes().First();

    private readonly List<FleetRow?> _rows = [];

    public FleetRowSource(IEnumerable<FleetRow> rows, bool spaced = false)
    {
        var items = rows.ToList();

        Items = items.Count;
        Stride = spaced ? 2 : 1;

        for (var i = 0; i < items.Count; i++)
        {
            _rows.Add(items[i]);

            if (spaced && i < items.Count - 1)
            {
                _rows.Add(null);
            }
        }
    }

    public event NotifyCollectionChangedEventHandler? CollectionChanged
    {
        add { }
        remove { }
    }

    public int Items { get; }

    public int Stride { get; }

    public int Count => _rows.Count;

    public int MaxItemLength => _rows.Count == 0
        ? 0
        : _rows.Max(r => r?.Text.Length ?? 0);

    public bool SuspendCollectionChangedEvent { get; set; }

    public void Dispose() => GC.SuppressFinalize(this);

    public bool IsMarked(int item) => false;

    public void SetMark(int item, bool value) { }

    public IList ToList() => _rows.Select(r => r?.Text ?? string.Empty).ToList();

    public bool RenderMark(
        ListView listView, int item, int row, bool isMarked, bool markMultiple) => true;

    public bool Holds(int index) =>
        index >= 0 && index < _rows.Count && _rows[index] is not null;

    public int ItemAt(int index) => Math.Max(0, index) / Stride;

    public int IndexOf(int item) => item * Stride;

    public void Replace(int item, FleetRow row)
    {
        var index = IndexOf(item);

        if (index >= 0 && index < _rows.Count)
        {
            _rows[index] = row;
        }
    }

    public void Render(
        ListView listView,
        bool selected,
        int item,
        int col,
        int row,
        int width,
        int viewportX)
    {
        var normal = listView.GetAttributeForRole(VisualRole.Normal);
        var entry = item >= 0 && item < _rows.Count ? _rows[item] : null;

        listView.Move(col, row);

        if (entry is null)
        {
            Fill(listView, normal, 0, width);
            return;
        }

        var basis = selected
            ? listView.GetAttributeForRole(VisualRole.Focus)
            : normal;

        var drawn = 0;
        var room = Math.Max(0, width - 1);

        if (selected)
        {
            listView.SetAttribute(new Attribute(basis.Background, normal.Background));
            listView.AddRune(CapLeft);
            drawn = 1;
        }

        drawn = Draw(listView, entry.Spans, basis, Math.Max(0, viewportX), room, drawn);

        if (entry.Trailing is { Count: > 0 })
        {
            var tail = entry.Trailing.Sum(s => s.Text.Length);
            var gap = Math.Max(1, room - drawn - tail);

            drawn = Fill(listView, basis, drawn, Math.Min(room, drawn + gap));
            drawn = Draw(listView, entry.Trailing, basis, 0, room, drawn);
        }

        drawn = Fill(listView, basis, drawn, room);

        if (drawn < width)
        {
            listView.SetAttribute(selected
                ? new Attribute(basis.Background, normal.Background)
                : normal);

            listView.AddRune(selected ? CapRight : Blank);
        }
    }

    private static int Fill(ListView listView, Attribute attribute, int drawn, int upTo)
    {
        listView.SetAttribute(attribute);

        while (drawn < upTo)
        {
            listView.AddRune(Blank);
            drawn++;
        }

        return drawn;
    }

    private static int Draw(
        ListView listView,
        IReadOnlyList<FleetSpan> spans,
        Attribute basis,
        int skip,
        int width,
        int drawn)
    {
        foreach (var span in spans)
        {
            var attribute = FleetInk.For(span.Tone, basis);

            foreach (var rune in span.Text.EnumerateRunes())
            {
                if (skip > 0)
                {
                    skip--;
                    continue;
                }

                if (drawn >= width)
                {
                    return drawn;
                }

                listView.SetAttribute(attribute);
                listView.AddRune(rune);
                drawn++;
            }
        }

        return drawn;
    }
}
