using Fleet.Shared.Keymap.Enums;

namespace Fleet.Shared.Keymap.Models;

public sealed record MenuEntry(FleetAction Action, string Key, string Label);
