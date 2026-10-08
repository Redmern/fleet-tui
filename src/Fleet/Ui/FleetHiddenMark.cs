using Fleet.Ui.Constants;
using Fleet.Ui.Models;

namespace Fleet.Ui;

public static class FleetHiddenMark
{
    public static readonly FleetSpan Shown = FleetSpan.Muted($"{FleetGlyphs.Hidden} ");

    public static readonly FleetSpan Visible = FleetSpan.Muted($"{FleetIcons.Show} ");

    public static FleetSpan For(bool hidden) => hidden ? Shown : Visible;

    public static bool Is(FleetSpan span) =>
        span.Text.Contains(FleetGlyphs.Hidden, StringComparison.Ordinal)
        || span.Text.Contains(FleetIcons.Show, StringComparison.Ordinal);
}
