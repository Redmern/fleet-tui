using Fleet.Cli.Composition;
using Fleet.Features.Updates.CheckUpdate;
using Fleet.Shared.Constants;

namespace Fleet.Cli.Commands;

public static class VersionCommand
{
    public static async Task<int> RunAsync()
    {
        Console.WriteLine($"fleet {FleetVersion.Current}");

        var check = await new CheckUpdateHandler(Adapters.Releases())
            .HandleAsync(Adapters.ReleaseRepo, FleetVersion.Current)
            .ConfigureAwait(false);

        if (check.Error is not null)
        {
            Console.WriteLine($"  update check failed: {check.Error}");
        }
        else if (check.UpdateAvailable)
        {
            Console.WriteLine($"  {check.Latest} is available — run 'fleet update'");
        }
        else
        {
            Console.WriteLine("  up to date");
        }

        return 0;
    }
}
