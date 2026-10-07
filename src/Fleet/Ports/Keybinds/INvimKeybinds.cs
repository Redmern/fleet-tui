using Fleet.Shared.Keybinds;

namespace Fleet.Ports.Keybinds;

public interface INvimKeybinds
{
    string GeneratedFile { get; }

    bool WriteGenerated(KeybindSet set);

    bool WriteUserModule(KeybindSet set, string path);

    bool GeneratedIsCurrent(KeybindSet set);

    bool UserModuleIsCurrent(KeybindSet set, string path);
}
