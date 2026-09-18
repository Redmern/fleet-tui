using Fleet.Ports.Projects.Models;

namespace Fleet.Features.Projects.OpenProject.Models;

public sealed record OpenProjectCommand(
    Project Project,
    string Harness,
    string FleetExecutable,
    string? WindowId = null,
    string? Workspace = null);
