using Fleet.Ports.Projects.Models;
using Fleet.Shared.Keymap.Enums;

namespace Fleet.Features.Projects.PickProject.Models;

public sealed record PickProjectCallbacks(
    Func<Project?> CreateProject,
    Func<FleetAction> ShowMenu,
    Action EditKeybinds);
