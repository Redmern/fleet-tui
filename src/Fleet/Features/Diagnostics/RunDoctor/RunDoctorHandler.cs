using Fleet.Features.Diagnostics.RunDoctor.Models;
using Fleet.Ports;
using Fleet.Ports.Mux;
using Fleet.Ports.Projects;

namespace Fleet.Features.Diagnostics.RunDoctor;

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
