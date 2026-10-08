using Fleet.Ports.Releases.Models;

namespace Fleet.Ports.Releases;

public interface IUpdateCheckCache
{
    CachedUpdateCheck? Load();

    void Save(CachedUpdateCheck check);
}
