using Fleet.Ports.Keymap;
using Fleet.Shared.Keymap.Models;

namespace Fleet.Cli.Composition;

public sealed class ApplyingKeymapStore(IKeymapStore inner, Action applied) : IKeymapStore
{
    public KeymapConfig Load() => inner.Load();

    public void Save(KeymapConfig config)
    {
        inner.Save(config);
        applied();
    }
}
