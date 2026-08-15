using Fleet.Ports.Settings;
using Fleet.Shared.Results;
using Fleet.Shared.Settings.Models;

namespace Fleet.Platform.Harness;

public sealed class NullSettingsSync : ISettingsSync
{
    public Result Resync(string project, string projectRoot, SettingsConfig config) => Result.Ok();
}
