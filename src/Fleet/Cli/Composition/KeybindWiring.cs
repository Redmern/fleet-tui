using System.Text.Json;
using Fleet.Platform.Keybinds;
using Fleet.Ports.Keybinds.Enums;
using Fleet.Platform.Keybinds.Models;
using Fleet.Ports.Keybinds.Models;
using Fleet.Platform.Storage;
using Fleet.Shared.Keybinds;
using Fleet.Shared.Keybinds.Enums;

namespace Fleet.Cli.Composition;

public static class KeybindWiring
{
    public const string FixCommand = "fleet apply-keybinds";

    public static IReadOnlyList<KeybindTarget> Rendered => KeybindDistribution.Rendered;

    public static IReadOnlyList<KeybindApplied> Apply(IReadOnlyCollection<KeybindTarget> only, bool dryRun)
    {
        var log = Adapters.Log();

        return Distribution(log.Write).Apply(only, dryRun);
    }

    public static IReadOnlyList<KeybindApplied> ApplyAll() => Apply(KeybindDistribution.Rendered, dryRun: false);

    public static IReadOnlyList<KeybindApplied> Drift() => Distribution(null).Drift();

    public static void ApplyQuietly()
    {
        var log = Adapters.Log();

        try
        {
            foreach (var failed in ApplyAll().Where(a => a.Outcome == KeybindOutcome.Failed))
            {
                log.Write("keybinds: " + failed.Line);
            }
        }
        catch (Exception e)
        {
            log.Swallowed(e);
        }
    }

    private static KeybindDistribution Distribution(Action<string>? log) =>
        new(Load(log), KeybindTargetPaths.Resolve(Folders(), Adapters.HomeDirectory), log: log);

    private static KeybindSet Load(Action<string>? log)
    {
        try
        {
            return new JsonKeybindStore(log).Load();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or NotSupportedException or InvalidOperationException)
        {
            log?.Invoke($"keybinds: {FleetPaths.KeymapFile} not read ({e.Message}); using the shipped keybinds");
            return KeybindDefaults.Set;
        }
    }

    private static IEnumerable<string> Folders() =>
        [Environment.CurrentDirectory, .. Adapters.Projects().List().Select(p => p.Root)];
}
