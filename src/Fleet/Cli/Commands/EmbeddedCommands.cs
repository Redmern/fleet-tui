using Fleet.Cli.Composition;
using Fleet.Cli.Models;

namespace Fleet.Cli.Commands;

public static class EmbeddedCommands
{
    public static Task<int> DaemonAsync() => EmbeddedWiring.RunDaemonAsync(Adapters.Log());

    public static Task<int> AttachAsync(Invocation invocation) =>
        EmbeddedWiring.AttachAsync(invocation.Project, invocation.Ssh, Adapters.Log());

    public static Task<int> BridgeAsync() => EmbeddedWiring.BridgeAsync();

    public static Task<int> CliAsync(IReadOnlyList<string> args) => EmbeddedWiring.CliAsync(args);
}
