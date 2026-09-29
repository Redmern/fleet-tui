using Fleet.Cli.Composition;
using Fleet.Cli.Models;

namespace Fleet.Cli.Commands;

public static class EmbeddedCommands
{
    public const string StopArgument = "stop";

    public static Task<int> DaemonAsync(Invocation invocation) =>
        invocation.Text switch
        {
            null => EmbeddedWiring.RunDaemonAsync(Adapters.Log()),
            StopArgument => EmbeddedWiring.StopDaemonAsync(),
            var other => Task.FromResult(HelpCommand.Unknown($"daemon {other}")),
        };

    public static Task<int> AttachAsync(Invocation invocation) =>
        EmbeddedWiring.AttachAsync(invocation.Project, invocation.Ssh, Adapters.Log());

    public static Task<int> BridgeAsync() => EmbeddedWiring.BridgeAsync();

    public static Task<int> CliAsync(IReadOnlyList<string> args) => EmbeddedWiring.CliAsync(args);
}
