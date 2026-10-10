using Fleet.Cli.Composition;
using Fleet.Cli.Models;
using Fleet.Features.Forwards.ForwardPorts;

namespace Fleet.Cli.Commands;

public static class ForwardCommand
{
    public static async Task<int> RunAsync(Invocation invocation)
    {
        var order = ForwardOrders.Parse(invocation.Arguments ?? []);
        if (!order.Succeeded)
        {
            await Console.Error.WriteLineAsync(order.Error).ConfigureAwait(false);
            return 2;
        }

        var handler = new ForwardPortsHandler(Adapters.Forwards(), Adapters.Browser(), Adapters.KnownRemotes(), Environment.UserName);
        var result = await handler.HandleAsync(order.Value).ConfigureAwait(false);

        if (!result.Succeeded)
        {
            await Console.Error.WriteLineAsync($"fleet forward: {result.Error}").ConfigureAwait(false);
            return 1;
        }

        await Console.Out.WriteLineAsync(result.Value).ConfigureAwait(false);
        return 0;
    }
}
