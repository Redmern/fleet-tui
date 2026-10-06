using Fleet.Shared.Keymap.Enums;

namespace Fleet.Features.Menu.ShowMenu.Models;

public sealed record FleetMenuItem(
    FleetAction Action,
    string Label,
    string KeyText,
    string? Header = null,
    string? Value = null,
    bool OpensMore = false,
    bool Toggles = false);
