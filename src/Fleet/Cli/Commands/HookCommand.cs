using Fleet.Cli.Composition;
using Fleet.Features.Agents.TrackStatus;
using Fleet.Ports.Agents;
using Fleet.Shared.Hooks;
using Fleet.Shared.Status;

namespace Fleet.Cli.Commands;

public static class HookCommand
{
    public static async Task<int> RunAsync()
    {
        try
        {
            await HandleAsync(Adapters.ReadHookEvent(), Adapters.AgentStates(), Adapters.SessionEnds(), DateTime.UtcNow)
                .ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Adapters.Log().Swallowed(e);
        }

        return 0;
    }

    public static async Task HandleAsync(
        HookEvent hook, IAgentStateStore states, SessionEnds ends, DateTime now, CancellationToken ct = default)
    {
        if (ClosingMark.FolderCovering(hook.Cwd) is { } closing)
        {
            if (hook.Name != HookStatus.SessionStartEvent)
            {
                return;
            }

            ClosingMark.Clear(closing);
        }

        await new TrackStatusHandler(states).HandleAsync(hook, now, ct).ConfigureAwait(false);
        await ends.HandleAsync(hook, ct).ConfigureAwait(false);
    }
}
