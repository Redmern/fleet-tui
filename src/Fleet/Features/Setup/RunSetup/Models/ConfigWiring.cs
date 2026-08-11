using Fleet.Features.Setup.RunSetup.Enums;

namespace Fleet.Features.Setup.RunSetup.Models;

public sealed record ConfigWiring(WiringState State, string Path, string Detail = "");
