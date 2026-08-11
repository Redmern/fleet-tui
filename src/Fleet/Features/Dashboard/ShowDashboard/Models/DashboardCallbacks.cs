using Fleet.Ports.Agents.Models;
using Fleet.Shared.Keymap.Enums;
using Fleet.Ui;
using Fleet.Ui.Models;

using Fleet.Shared;

namespace Fleet.Features.Dashboard.ShowDashboard.Models;

public sealed record DashboardCallbacks(
    Func<Task<IReadOnlyList<RepositoryChoice>>> LoadRepositories,
    Func<Task<string?>> AddRepository,
    Func<FleetAction> ShowMenu,
    Func<Keymap> EditKeybinds,
    Func<Keymap> ReloadKeymap,
    Action ShowLogs,
    Func<FleetAction> TakeRequest,
    Func<AgentBoard> LoadAgents,
    Func<IReadOnlyList<RepositoryChoice>, int, Task<string?>> NewAgent,
    Func<int, Task<string?>> OpenAgent,
    Func<int, string?> ManageAgent,
    Func<int, string?> HideAgent,
    Func<RepositoryChoice, string?> RemoveRepository,
    Func<RepositoryChoice, Task<string?>> PullRepository,
    Func<RepositoryChoice, RepositoryManaged> ManageRepository,
    Func<RepositoryChoice, Task<string?>> OpenRepository,
    Func<AgentRecord, BranchState> AgentState,
    Func<RepositoryChoice, BranchState> RepositoryState);
