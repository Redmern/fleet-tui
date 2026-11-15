using Fleet.Shared.Keymap.Enums;

namespace Fleet.Features.Menu.ShowMenu.Models;

public sealed record MenuScreen(FleetAction Submenu, string Title, IReadOnlyList<MenuSection> Sections, string? Note = null);
