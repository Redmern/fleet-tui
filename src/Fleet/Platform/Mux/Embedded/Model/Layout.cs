namespace Fleet.Platform.Mux.Embedded.Model;

public abstract record Layout
{
    public static Layout Of(string pane) => new LayoutLeaf(pane);

    public abstract bool Contains(string pane);

    public abstract IEnumerable<string> Panes();

    public Layout Split(string target, string added, bool sideBySide, bool addedFirst, int percent) =>
        this switch
        {
            LayoutLeaf leaf when leaf.Pane == target => new LayoutSplit(
                sideBySide,
                Ratio(addedFirst ? Share(percent) : 100 - Share(percent)),
                addedFirst ? new LayoutLeaf(added) : leaf,
                addedFirst ? leaf : new LayoutLeaf(added)),
            LayoutSplit split => split with
            {
                First = split.First.Split(target, added, sideBySide, addedFirst, percent),
                Second = split.Second.Split(target, added, sideBySide, addedFirst, percent),
            },
            _ => this,
        };

    public Layout? Remove(string pane) => this switch
    {
        LayoutLeaf leaf => leaf.Pane == pane ? null : leaf,
        LayoutSplit split => (split.First.Remove(pane), split.Second.Remove(pane)) switch
        {
            (null, var second) => second,
            (var first, null) => first,
            var (first, second) => split with { First = first, Second = second },
        },
        _ => this,
    };

    public void Place(Rect area, List<Placed> panes, List<Divider> dividers)
    {
        switch (this)
        {
            case LayoutLeaf leaf:
                panes.Add(new Placed(leaf.Pane, area));
                break;
            case LayoutSplit split:
            {
                var total = split.SideBySide ? area.Width : area.Height;
                var usable = Math.Max(0, total - 1);
                var first = Math.Clamp((int)Math.Round(usable * split.Ratio), 1, Math.Max(1, usable - 1));
                var second = Math.Max(0, usable - first);

                if (split.SideBySide)
                {
                    split.First.Place(area with { Width = first }, panes, dividers);
                    dividers.Add(new Divider(area.X + first, area.Y, true, area.Height));
                    split.Second.Place(area with { X = area.X + first + 1, Width = second }, panes, dividers);
                }
                else
                {
                    split.First.Place(area with { Height = first }, panes, dividers);
                    dividers.Add(new Divider(area.X, area.Y + first, false, area.Width));
                    split.Second.Place(area with { Y = area.Y + first + 1, Height = second }, panes, dividers);
                }

                break;
            }
        }
    }

    private static int Share(int percent) => percent <= 0 ? 50 : percent;

    private static double Ratio(int percent) => Math.Clamp(percent, 5, 95) / 100.0;
}

public sealed record LayoutLeaf(string Pane) : Layout
{
    public override bool Contains(string pane) => Pane == pane;

    public override IEnumerable<string> Panes() => [Pane];
}

public sealed record LayoutSplit(bool SideBySide, double Ratio, Layout First, Layout Second) : Layout
{
    public override bool Contains(string pane) => First.Contains(pane) || Second.Contains(pane);

    public override IEnumerable<string> Panes() => First.Panes().Concat(Second.Panes());
}

public readonly record struct Rect(int X, int Y, int Width, int Height)
{
    public bool Contains(int x, int y) => x >= X && x < X + Width && y >= Y && y < Y + Height;
}

public readonly record struct Placed(string Pane, Rect Area);

public readonly record struct Divider(int X, int Y, bool Vertical, int Length);
