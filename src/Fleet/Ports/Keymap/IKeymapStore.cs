using Fleet.Shared.Keymap.Models;

namespace Fleet.Ports.Keymap;

public interface IKeymapStore
{
    KeymapConfig Load();

    void Save(KeymapConfig config);
}
