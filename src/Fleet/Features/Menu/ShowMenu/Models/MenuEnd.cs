using Fleet.Shared.Keymap.Enums;

namespace Fleet.Features.Menu.ShowMenu.Models;

public readonly record struct MenuEnd(int Code, FleetAction Pick = FleetAction.None);
