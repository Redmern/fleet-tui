using Fleet.Ui.Constants;
using Fleet.Ui.Models;

namespace Fleet.Ui;

public static class FleetHiddenMark
{
    public static readonly FleetSpan Shown = FleetSpan.Muted($"{FleetGlyphs.Hidden} ");

    public static readonly FleetSpan Blank = FleetSpan.Muted(new string(' ', Shown.Text.EnumerateRunes().Count()));

    public static FleetSpan For(bool hidden) => hidden ? Shown : Blank;

    public static bool Is(FleetSpan span) =>
        span == Blank || span.Text.Contains(FleetGlyphs.Hidden, StringComparison.Ordinal);
}
