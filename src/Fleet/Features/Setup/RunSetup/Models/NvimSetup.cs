namespace Fleet.Features.Setup.RunSetup.Models;

public sealed record NvimSetup(bool FleetConfig, bool Installed, string Directory, Version? Version);
