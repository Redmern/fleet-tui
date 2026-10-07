using Fleet.Ui.Constants;
using Fleet.Ui.Models;

namespace Fleet.Ui;

public static class StatusIcon
{
    public static FleetSpan? For(string status) => status.Trim().ToLowerInvariant() switch
    {
        "working" => new FleetSpan(FleetGlyphs.Working, FleetTones.Warn),
        "waiting" => new FleetSpan(FleetGlyphs.Waiting, FleetTones.Bad),
        "stalled" => new FleetSpan(FleetGlyphs.Stalled, FleetTones.Bad),
        "idle" => new FleetSpan(FleetGlyphs.Idle, FleetTones.Good),
        "done" => new FleetSpan(FleetGlyphs.Done, FleetTones.Good),
        "failed" => new FleetSpan(FleetGlyphs.Failed, FleetTones.Bad),
        _ => null,
    };
}
