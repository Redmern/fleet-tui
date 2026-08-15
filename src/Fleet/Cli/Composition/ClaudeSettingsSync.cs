using Fleet.Ports.Settings;
using Fleet.Shared.Results;
using Fleet.Shared.Settings.Models;

namespace Fleet.Cli.Composition;

public sealed class ClaudeSettingsSync : ISettingsSync
{
    public Result Resync(string project, string projectRoot, SettingsConfig config) =>
        ClaudeWiring.Sync(project, projectRoot, string.Empty, config.MergedOverDefaults());
}
