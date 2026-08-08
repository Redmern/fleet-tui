using Fleet.Ports.Mux;
using Fleet.Ports.Projects;
using Fleet.Shared;

namespace Fleet.Features.Projects.OpenProject;

/// <param name="Harness">
/// The AI CLI for the left pane. Hardcoded to "claude" by the composition root in
/// phase 1; harness.d/*.toml is the eventual answer.
/// </param>
/// <param name="FleetExecutable">
/// The binary the dashboard pane should invoke. Must be this process's own path,
/// not the literal string "fleet" — during development nothing named fleet
/// resolves on PATH.
/// </param>
public sealed record OpenProjectCommand(Project Project, string Harness, string FleetExecutable);

public sealed record OpenProjectResult(PaneId HarnessPane, PaneId DashPane);

/// <summary>
/// Opens a project: one window at the project root, the AI harness on the left,
/// the fleet dashboard on the right, focus left on the dashboard.
/// </summary>
public sealed class OpenProjectHandler(IMuxDriver mux)
{
    public async Task<Result<OpenProjectResult>> HandleAsync(
        OpenProjectCommand command, CancellationToken ct = default)
    {
        var harnessPane = await mux.SpawnAsync(
            new SpawnOptions
            {
                NewWindow = true,
                SessionName = command.Project.Name,
                Cwd = command.Project.Root,
                Args = [command.Harness],
            },
            ct).ConfigureAwait(false);

        // FailSilentDriver returns PaneId.None rather than throwing when the mux is
        // unreachable, so this is the failure check — not a null guard.
        if (harnessPane.IsNone)
        {
            return Unreachable();
        }

        await mux.SetTitleAsync(harnessPane, command.Project.Name, ct).ConfigureAwait(false);

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
