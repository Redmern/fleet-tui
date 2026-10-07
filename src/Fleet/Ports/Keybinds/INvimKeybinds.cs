using Fleet.Shared.Keybinds;

namespace Fleet.Ports.Keybinds;

public interface INvimKeybinds
{
    string GeneratedFile { get; }

    bool WriteGenerated(KeybindSet set);

    bool WriteUserModule(KeybindSet set, string path);
}
