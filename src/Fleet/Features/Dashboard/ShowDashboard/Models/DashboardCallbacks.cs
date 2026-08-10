using Fleet.Shared.Keymap.Enums;

namespace Fleet.Features.Dashboard.ShowDashboard.Models;

public sealed record DashboardCallbacks(
    Func<Task<IReadOnlyList<RepositoryChoice>>> LoadRepositories,
    Func<Task<string?>> AddRepository,
    Func<FleetAction> ShowMenu,
    Action EditKeybinds,
    Func<FleetAction> TakeRequest,
    Func<(IReadOnlyList<string> Rows, int Count)> LoadAgents,
    Func<IReadOnlyList<RepositoryChoice>, int, Task<string?>> NewAgent,
    Func<int, Task<string?>> OpenAgent,
    Func<int, string?> ManageAgent,
    Func<RepositoryChoice, string?> RemoveRepository);
