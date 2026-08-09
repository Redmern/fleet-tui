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
        FleetVerb.Doctor => await DoctorCommand.RunAsync().ConfigureAwait(false),
        FleetVerb.Help => HelpCommand.Run(),
        _ => HelpCommand.Unknown(invocation.Raw),
    };
}
