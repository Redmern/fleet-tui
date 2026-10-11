using Fleet.Cli.Composition;
using Fleet.Cli.Models;

namespace Fleet.Cli.Commands;

public static class ShimCommand
{
    public static Task<int> RunAsync(Invocation invocation) => EmbeddedWiring.ShimAsync(invocation.Arguments ?? []);
}
