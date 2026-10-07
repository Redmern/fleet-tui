using System.Text.Json;
using Fleet.Platform.Mux.Embedded.Input;
using Fleet.Shared.Keybinds;

namespace Fleet.Platform.Storage;

public sealed class JsonKeybindStore(Action<string>? log = null)
{
    private readonly Action<string> _log = log ?? (_ => { });

    public static string MuxKeysFile => Path.Combine(FleetPaths.Config, MuxKeys.FileName);

    public KeybindSet Load()
    {
        var set = KeybindDefaults.Set;
        var keymap = JsonKeymapStore.Read();

        if (keymap is not null)
        {
            set = KeybindLegacy.FleetUi(set, keymap.Prefix, keymap.Bindings);
        }

        if (ReadMux() is { } mux)
        {
            set = KeybindLegacy.Mux(set, mux.Prefix, mux.PrefixKeys, mux.Keys, _log);
        }

        return KeybindLayering.Apply(set, keymap?.Keybinds, _log);
    }

    private MuxKeysFile? ReadMux()
    {
        try
        {
            return File.Exists(MuxKeysFile)
                ? JsonSerializer.Deserialize(File.ReadAllText(MuxKeysFile), MuxKeysJsonContext.Default.MuxKeysFile)
                : null;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            _log($"keybinds: {MuxKeysFile} not migrated ({e.Message})");
            return null;
        }
    }
}
