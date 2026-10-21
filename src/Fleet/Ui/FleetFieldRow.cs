using Fleet.Ui.Constants;
using Fleet.Ui.Models;

namespace Fleet.Ui;

public static class FleetFieldRow
{
    private const int LabelWidth = 9;

    public static FleetRow Of(string icon, string label, string value) =>
        new(
        [
            new FleetSpan(icon, FleetTones.Icon),
            FleetSpan.Plain(" "),
            new FleetSpan($"{label}:".PadRight(LabelWidth), FleetTones.Key),
            FleetSpan.Plain(value),
        ]);
}
