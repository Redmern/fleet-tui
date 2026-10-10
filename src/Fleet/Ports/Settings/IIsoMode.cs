using Fleet.Shared.Iso.Models;

namespace Fleet.Ports.Settings;

public interface IIsoMode
{
    IsoConfig Load();

    void Save(IsoConfig config);
}
