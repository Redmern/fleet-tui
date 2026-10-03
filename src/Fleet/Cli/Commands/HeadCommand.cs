using Fleet.Cli.Composition;
using Fleet.Cli.Models;
using Fleet.Features.Head.ServeHead;

namespace Fleet.Cli.Commands;

public static class HeadCommand
{
    public static int Run(Invocation invocation)
    {
        Console.CancelKeyPress += (_, e) => e.Cancel = true;

        return HeadWiring.Launch(invocation.Arguments?.Contains(HeadLaunch.VoiceFlag) == true);
    }
}
