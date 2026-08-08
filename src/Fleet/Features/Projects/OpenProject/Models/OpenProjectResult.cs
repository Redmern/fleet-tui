using Fleet.Ports.Mux.Models;

namespace Fleet.Features.Projects.OpenProject.Models;

public sealed record OpenProjectResult(PaneId HarnessPane, PaneId DashPane);
