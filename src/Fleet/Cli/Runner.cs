using Fleet.Cli.Commands;
using Fleet.Cli.Enums;
using Fleet.Cli.Models;

namespace Fleet.Cli;

public static class Runner
{
    public static async Task<int> RunAsync(Invocation invocation) => invocation.Verb switch
    {
        FleetVerb.Pick => await PickProjectCommand.RunAsync().ConfigureAwait(false),
        FleetVerb.Dash => DashCommand.Run(invocation),
        FleetVerb.Menu => await MenuCommand.RunAsync(invocation).ConfigureAwait(false),
        FleetVerb.Request => RequestCommand.Run(invocation),
        FleetVerb.ApplyKeybinds => ApplyKeybindsCommand.Run(),
        FleetVerb.Setup => SetupCommand.Run(),
        FleetVerb.Dispatch => await DispatchCommand.RunAsync(invocation).ConfigureAwait(false),
        FleetVerb.HookDispatch => await HookDispatchCommand.RunAsync(invocation).ConfigureAwait(false),
        FleetVerb.Hook => await HookCommand.RunAsync().ConfigureAwait(false),
        FleetVerb.Report => ReportCommand.Run(invocation),
        FleetVerb.Mcp => await McpCommand.RunAsync(invocation).ConfigureAwait(false),
        FleetVerb.Head => HeadCommand.Run(invocation),
        FleetVerb.Quit => await QuitCommand.RunAsync(invocation).ConfigureAwait(false),
        FleetVerb.Doctor => await DoctorCommand.RunAsync().ConfigureAwait(false),
        FleetVerb.Version => await VersionCommand.RunAsync().ConfigureAwait(false),
        FleetVerb.Update => await UpdateCommand.RunAsync(invocation).ConfigureAwait(false),
        FleetVerb.Titled => TitledCommand.Run(invocation),
        FleetVerb.WithEnv => WithEnvCommand.Run(invocation),
        FleetVerb.Daemon => await EmbeddedCommands.DaemonAsync(invocation).ConfigureAwait(false),
        FleetVerb.Attach => await EmbeddedCommands.AttachAsync(invocation).ConfigureAwait(false),
        FleetVerb.Bridge => await EmbeddedCommands.BridgeAsync().ConfigureAwait(false),
        FleetVerb.Approve => await ApproveCommand.RunAsync(invocation).ConfigureAwait(false),
        FleetVerb.Cli => await EmbeddedCommands.CliAsync(invocation.Arguments ?? []).ConfigureAwait(false),
        FleetVerb.AskPass => await EmbeddedCommands.AskPassAsync(invocation).ConfigureAwait(false),
        FleetVerb.Help => HelpCommand.Run(),
        _ => HelpCommand.Unknown(invocation.Raw),
    };
}
