using Fleet.Shared.Keymap.Enums;
using Fleet.Ui.Enums;

namespace Fleet.Ui.Models;

public sealed record PrefixResult(PrefixOutcome Outcome, FleetAction Action)
{
    public static readonly PrefixResult NotForFleet =
        new(PrefixOutcome.NotForFleet, FleetAction.None);

    public static readonly PrefixResult Armed = new(PrefixOutcome.Armed, FleetAction.None);

    public static readonly PrefixResult Cancelled = new(PrefixOutcome.Cancelled, FleetAction.None);

    public static PrefixResult For(FleetAction action) => new(PrefixOutcome.Action, action);

    public bool Handled => Outcome != PrefixOutcome.NotForFleet;
}
