namespace Fleet.Platform.Mux.Embedded.Render;

public readonly record struct Viewport(long Top, long Total, int Rows, bool AtBottom, bool AltScreen)
{
    public static readonly Viewport Live = new(0, 0, 0, true, false);

    public long History => Math.Max(0, Total - Rows);

    public long Below => Math.Max(0, Total - Top - Rows);
}

public readonly record struct TextPoint(long Row, int Col) : IComparable<TextPoint>
{
    public int CompareTo(TextPoint other) => Row != other.Row ? Row.CompareTo(other.Row) : Col.CompareTo(other.Col);
}