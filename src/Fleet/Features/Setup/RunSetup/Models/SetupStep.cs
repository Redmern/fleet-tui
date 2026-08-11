namespace Fleet.Features.Setup.RunSetup.Models;

public sealed record SetupStep(
    string Name, bool Ok, string Detail, bool Required = false, string Fix = "");
