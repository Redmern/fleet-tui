using Fleet.Ports.Projects.Models;
using Fleet.Shared.Settings.Models;

namespace Fleet.Features.Projects.OpenProject.Models;

public sealed record OpenProjectCommand(
    Project Project,
    string Harness,
    string FleetExecutable,
    string? WindowId = null,
    bool MainOrchestratorInNvim = true,
    RoleModels? Models = null);
