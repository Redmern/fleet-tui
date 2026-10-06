using Fleet.Shared.Keymap.Enums;

namespace Fleet.Features.Menu.ShowMenu.Models;

public sealed record MenuSection(string? Header, IReadOnlyList<FleetAction> Actions, string? Icon = null);
