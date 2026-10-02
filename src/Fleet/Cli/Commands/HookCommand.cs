using Fleet.Cli.Composition;
using Fleet.Features.Agents.TrackStatus;

namespace Fleet.Cli.Commands;

public static class HookCommand
{
    public static async Task<int> RunAsync()
    {
        try
        {
            await new TrackStatusHandler(Adapters.AgentStates())
                .HandleAsync(Adapters.ReadHookEvent(), DateTime.UtcNow)
                .ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Adapters.Log().Swallowed(e);
        }

        return 0;
    }
}
