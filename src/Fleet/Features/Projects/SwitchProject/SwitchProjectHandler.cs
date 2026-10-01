using Fleet.Ports.Mux;
using Fleet.Ports.Mux.Enums;
using Fleet.Shared.Results;

namespace Fleet.Features.Projects.SwitchProject;

public sealed class SwitchProjectHandler(IMuxDriver mux)
{
    public static bool Applies(IMuxDriver mux) => mux.Caps.HasFlag(MuxCaps.Workspaces);

    public async Task<Result> HandleAsync(string target, CancellationToken ct = default)
    {
        var workspaces = await mux.ListWorkspacesAsync(ct).ConfigureAwait(false);

        if (!workspaces.Any(w => string.Equals(w.Name, target, StringComparison.OrdinalIgnoreCase)))
        {
            return Result.Fail($"{target} is not open.");
        }

        await mux.ShowWorkspaceAsync(target, ct).ConfigureAwait(false);

        return Result.Ok();
    }
}
