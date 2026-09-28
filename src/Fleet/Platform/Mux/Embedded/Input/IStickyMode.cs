using Fleet.Platform.Mux.Embedded.Native;
using Fleet.Platform.Mux.Embedded.Protocol;

namespace Fleet.Platform.Mux.Embedded.Input;

public interface IStickyMode
{
    bool Active { get; }

    CommandMessage? OnKey(Key key, Mods mods, string? text = null);

    int OnBytes(ReadOnlySpan<byte> bytes, out CommandMessage? command);
}