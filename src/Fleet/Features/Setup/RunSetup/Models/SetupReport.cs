namespace Fleet.Features.Setup.RunSetup.Models;

public sealed record SetupReport(IReadOnlyList<SetupStep> Steps)
{
    public bool Blocked => Steps.Any(s => s.Required && !s.Ok);

    public IReadOnlyList<SetupStep> Missing => [.. Steps.Where(s => !s.Ok)];
}
