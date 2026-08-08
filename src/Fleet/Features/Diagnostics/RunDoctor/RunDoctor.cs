using Fleet.Ports;
using Fleet.Ports.Mux;
using Fleet.Ports.Projects;

namespace Fleet.Features.Diagnostics.RunDoctor;

public sealed record RunDoctorCommand(string ChosenDriver, string? UnsupportedReason);

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

/// <summary>
/// The end-to-end smoke test, and the only thing the AOT CI job runs. It must
/// never need a terminal, a GUI, or a project.
///
/// Returns a report rather than printing one, which is what makes it testable;
/// the composition root does the printing.
/// </summary>
/// <param name="gitVersion">Returns git's version string, or null when git is missing.</param>
public sealed class RunDoctorHandler(
    IMuxDriver mux, IProjectStore projects, IFleetLog log, Func<Task<string?>> gitVersion)
{
    public async Task<DoctorReport> HandleAsync(
        RunDoctorCommand command, CancellationToken ct = default)
    {
        var problems = new List<string>();

        if (command.UnsupportedReason is not null)
        {
            problems.Add(command.UnsupportedReason);
        }

        var reachable = await mux.IsAvailableAsync(ct).ConfigureAwait(false);
        if (!reachable)
        {
            problems.Add($"the {command.ChosenDriver} multiplexer did not respond");
        }

        var version = await gitVersion().ConfigureAwait(false);
        if (version is null)
        {
            problems.Add("git was not found on PATH");
        }

        return new DoctorReport(
            command.ChosenDriver,
            reachable,
            version,
            projects.List(),
            log.Tail(5),
            problems);
    }
}
