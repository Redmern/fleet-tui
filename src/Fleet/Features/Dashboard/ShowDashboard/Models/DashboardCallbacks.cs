using Fleet.Ports.Agents.Models;
using Fleet.Ports.Approvals.Models;
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
    Action EditSettings,
    Action ShowLogs,
    Func<string?> BrowseFiles,
    Func<FleetAction> TakeRequest,
    Func<AgentBoard> LoadAgents,
    Func<SubBoard> LoadSubs,
    Func<IReadOnlyList<RepositoryChoice>, int, Task<string?>> NewAgent,
    Func<Task<string?>> DispatchSub,
    Func<int, int, Task<string?>> OpenAgent,
    Func<int, int, Task<string?>> ManageAgent,
    Func<int, int, string?> HideAgent,
    Func<int, IReadOnlyList<int>, string, Task<string?>> BatchAgents,
    Func<RepositoryChoice, Task<string?>> RemoveRepository,
    Func<RepositoryChoice, Task<string?>> PullRepository,
    Func<RepositoryChoice, Task<RepositoryManaged>> ManageRepository,
    Func<RepositoryChoice, Task<string?>> OpenRepository,
    Func<AgentRecord, BranchState> AgentState,
    Func<RepositoryChoice, BranchState> RepositoryState,
    Func<PendingApproval?> TakeApproval,
    Action<string, bool> AnswerApproval,
    Action Heartbeat);
