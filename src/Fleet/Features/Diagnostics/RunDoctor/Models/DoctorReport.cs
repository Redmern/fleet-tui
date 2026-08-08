using Fleet.Ports.Projects.Models;

namespace Fleet.Features.Diagnostics.RunDoctor.Models;

public sealed record DoctorReport(
    string ChosenDriver,
    bool MuxReachable,
    string? GitVersion,
    IReadOnlyList<Project> Projects,
    IReadOnlyList<string> RecentSwallowed,
    IReadOnlyList<string> Problems)
{
    public bool Healthy => Problems.Count == 0;
}
