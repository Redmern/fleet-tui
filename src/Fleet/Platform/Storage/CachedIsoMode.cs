using Fleet.Ports.Settings;
using Fleet.Shared.Iso.Models;

namespace Fleet.Platform.Storage;

public sealed class CachedIsoMode(IIsoMode inner) : IIsoMode
{
    private readonly Lock _gate = new();

    private (DateTime Stamp, IsoConfig Config)? _cached;

    public IsoConfig Load()
    {
        var stamp = Stamp();

        lock (_gate)
        {
            if (_cached is { } cached && cached.Stamp == stamp && stamp != DateTime.MinValue)
            {
                return cached.Config;
            }

            var config = inner.Load();
            _cached = (stamp, config);
            return config;
        }
    }

    public void Save(IsoConfig config)
    {
        inner.Save(config);

        lock (_gate)
        {
            _cached = null;
        }
    }

    private static DateTime Stamp()
    {
        try
        {
            return File.Exists(FleetPaths.IsoFile) ? File.GetLastWriteTimeUtc(FleetPaths.IsoFile) : DateTime.MinValue;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return DateTime.MinValue;
        }
    }
}
