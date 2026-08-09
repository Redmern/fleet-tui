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
    Func<int, string?> ChangeHarness,
    Func<int, Task<string?>> ToggleHidden,
    Func<int, Task<string?>> StopAgent,
    Func<int, string?> RemoveAgent);
