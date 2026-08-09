using Fleet.Cli.Composition;
using Fleet.Cli.Models;
using Fleet.Shared.Keymap;
using Fleet.Shared.Keymap.Enums;

namespace Fleet.Cli.Commands;

public static class RequestCommand
{
    public static int Run(Invocation invocation)
    {
        if (invocation.Project is null || invocation.Action is null)
        {
            Console.Error.WriteLine(
                "fleet request: --project <name> and --action <id> are required");

            return 2;
        }

        var action = FleetActionIds.Parse(invocation.Action);

        if (action == FleetAction.None)
        {
            Console.Error.WriteLine($"fleet request: unknown action '{invocation.Action}'");
            return 2;
        }

        Adapters.Requests().Submit(invocation.Project, action);
        return 0;
    }
}
