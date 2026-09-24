using Fleet.Features.Projects.OpenProject.Models;
using Fleet.Ports.Mux;
using Fleet.Ports.Mux.Enums;
using Fleet.Ports.Mux.Models;
using Fleet.Shared.Constants;
using Fleet.Shared.Results;

namespace Fleet.Features.Projects.OpenProject;

public sealed class OpenProjectHandler(IMuxDriver mux)
{
    public async Task<Result<OpenProjectResult>> HandleAsync(
        OpenProjectCommand command, CancellationToken ct = default)
    {
        var harnessPane = await mux.SpawnAsync(
            new SpawnOptions
            {
                NewWindow = command.WindowId is null,
                WindowId = command.WindowId,
                SessionName = command.Project.Name,
                Cwd = command.Project.Root,
                Args = AgentHarness.CommandFor(command.Harness),
            },
            ct).ConfigureAwait(false);

        if (harnessPane.IsNone)
        {
            return Unreachable();
        }

        await mux.SetTitleAsync(harnessPane, FleetTabTitles.Dashboard, ct).ConfigureAwait(false);

        var dashPane = await mux.SplitAsync(
            new SplitOptions(harnessPane, SplitDirection.Right)
            {
                Percent = 50,
                Cwd = command.Project.Root,
                Args = [command.FleetExecutable, "dash", "--project", command.Project.Name],
            },
            ct).ConfigureAwait(false);

        if (dashPane.IsNone)
        {
            return Unreachable();
        }

        await mux.FocusPaneAsync(dashPane, ct).ConfigureAwait(false);

        return Result<OpenProjectResult>.Ok(new OpenProjectResult(harnessPane, dashPane));
    }

    private Result<OpenProjectResult> Unreachable() =>
        Result<OpenProjectResult>.Fail(
            $"the {mux.Name} multiplexer did not respond. Run 'fleet doctor'.");
}
