using Fleet.Ports.Settings;
using Fleet.Shared.Iso.Models;

namespace Fleet.Tests;

public sealed class IsoSwitch(bool on = false) : IIsoMode
{
    public IsoConfig Config { get; set; } = IsoConfig.Off with { On = on };

    public IsoConfig Load() => Config;

    public void Save(IsoConfig config) => Config = config;
}
