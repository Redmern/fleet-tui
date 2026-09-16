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
        FleetVerb.Report => ReportCommand.Run(invocation),
        FleetVerb.Mcp => await McpCommand.RunAsync(invocation).ConfigureAwait(false),
        FleetVerb.Quit => await QuitCommand.RunAsync(invocation).ConfigureAwait(false),
        FleetVerb.Doctor => await DoctorCommand.RunAsync().ConfigureAwait(false),
        FleetVerb.Version => await VersionCommand.RunAsync().ConfigureAwait(false),
        FleetVerb.Update => await UpdateCommand.RunAsync().ConfigureAwait(false),
        FleetVerb.Titled => TitledCommand.Run(invocation),
        FleetVerb.Help => HelpCommand.Run(),
        _ => HelpCommand.Unknown(invocation.Raw),
    };
}
