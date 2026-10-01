using Fleet.Ports.Projects.Models;
using Fleet.Ports.Sessions.Models;
using Fleet.Shared.Keymap.Enums;

namespace Fleet.Features.Projects.PickProject.Models;

public sealed record PickProjectCallbacks(
    Func<Project?> CreateProject,
    Func<Project, string?> RemoveProject,
    Func<FleetAction> ShowMenu,
    Action EditKeybinds,
    Func<IReadOnlyList<WindowSession>>? Sessions = null,
    Func<WindowSession, string?>? RemoveSession = null);