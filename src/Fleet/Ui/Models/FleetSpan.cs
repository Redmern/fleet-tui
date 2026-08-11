using Fleet.Ui.Constants;

namespace Fleet.Ui.Models;

public sealed record FleetSpan(string Text, string Tone)
{
    public static FleetSpan Plain(string text) => new(text, FleetTones.Normal);

    public static FleetSpan Muted(string text) => new(text, FleetTones.Muted);
}
