using Fleet.Ports.Agents;
using Fleet.Shared.Status;

namespace Fleet.Features.Agents;

public static class AgentTraces
{
    public static async Task ForgetAsync(IAgentStateStore? hookState, string worktree, CancellationToken ct = default)
    {
        if (hookState is not null)
        {
            await hookState.ForgetAsync(worktree, ct).ConfigureAwait(false);
        }

        try
        {
            File.Delete(Path.Combine(worktree, ".fleet", PaneQuiet.FileName));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }
}
