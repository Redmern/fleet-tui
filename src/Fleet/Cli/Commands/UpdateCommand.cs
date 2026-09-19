using Fleet.Cli.Composition;
using Fleet.Cli.Models;
using Fleet.Features.Updates.RunUpdate;
using Fleet.Features.Updates.RunUpdate.Models;
using Fleet.Shared.Constants;
using Fleet.Shared.Releases;

namespace Fleet.Cli.Commands;

public static class UpdateCommand
{
    public static async Task<int> RunAsync(Invocation invocation)
    {
        var repo = Adapters.ReleaseRepo;

        Console.WriteLine(invocation.Version is null
            ? $"fleet: checking github.com/{repo}/releases for an update..."
            : $"fleet: checking github.com/{repo}/releases for {invocation.Version}...");

        var result = await new RunUpdateHandler(Adapters.Releases(), Adapters.Installer())
            .HandleAsync(new RunUpdateCommand(
                repo,
                FleetVersion.Current,
                ReleaseAssetNames.ForCurrentPlatform(),
                Adapters.Executable,
                invocation.Version))
            .ConfigureAwait(false);

        if (!result.Succeeded)
        {
            Console.Error.WriteLine($"fleet: {result.Error}");
            return 1;
        }

        Console.WriteLine($"fleet: {result.Value}");
        return 0;
    }
}
