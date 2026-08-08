using Fleet.Shared.Keymap.Enums;

namespace Fleet.Features.Dashboard.ShowDashboard.Models;

public sealed record DashboardCallbacks(
    Func<Task<IReadOnlyList<(string Name, string DefaultBranch)>>> LoadRepositories,
    Func<Task<string?>> AddRepository,
    Func<FleetAction> ShowMenu,
    Action EditKeybinds,
    Func<FleetAction> TakeRequest);
