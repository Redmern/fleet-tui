using Fleet.Ports.Projects.Models;
using Fleet.Ports.Sessions.Models;

namespace Fleet.Features.Projects.PickProject.Models;

public sealed record ProjectPick(Project? Project, bool NewWindow, WindowSession? Session = null);