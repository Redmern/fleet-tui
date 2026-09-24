using System.Text;
using EmbeddedSpike.Ghostty;

namespace EmbeddedSpike.Input;

// libghostty-vt's key encoder. Before each key it copies the pane's current
// modes out of the terminal (DECCKM, keypad mode, kitty keyboard flags,
// modifyOtherKeys, alt-esc-prefix), so the bytes match whatever the program in
// the pane last asked for. Call under the terminal's Gate.
internal sealed unsafe class KeyEncoder : IDisposable
{
    private readonly VtTerminal _terminal;
    private readonly nint _encoder;
    private readonly nint _event;
    private readonly byte[] _buffer = new byte[128];

    public KeyEncoder(VtTerminal terminal)
    {
        _terminal = terminal;
        VtTerminal.Check(Native.KeyEncoderNew(0, out _encoder), "ghostty_key_encoder_new");
        VtTerminal.Check(Native.KeyEventNew(0, out _event), "ghostty_key_event_new");
    }

    public byte[] Encode(KeyInput input)
    {
        if (input.IsRawText)
        {
            return Encoding.UTF8.GetBytes(input.Utf8 ?? string.Empty);
        }

        Native.KeyEncoderSetoptFromTerminal(_encoder, _terminal.Handle);
        Native.KeyEventSetAction(_event, input.Action);
        Native.KeyEventSetKey(_event, input.Key);
        Native.KeyEventSetMods(_event, (ushort)input.Mods);
        Native.KeyEventSetConsumedMods(_event, (ushort)input.Consumed);
        Native.KeyEventSetComposing(_event, false);
        Native.KeyEventSetUnshiftedCodepoint(_event, input.Unshifted);

        var utf8 = input.Utf8 is null ? [] : Encoding.UTF8.GetBytes(input.Utf8);
        fixed (byte* text = utf8)
        {
            Native.KeyEventSetUtf8(_event, utf8.Length == 0 ? null : text, (nuint)utf8.Length);

            fixed (byte* output = _buffer)
            {
                var rc = Native.KeyEncoderEncode(_encoder, _event, output, (nuint)_buffer.Length, out var written);
                return rc == Native.Success ? _buffer.AsSpan(0, (int)written).ToArray() : [];
            }
        }
    }

    public void Dispose()
    {
        Native.KeyEventFree(_event);
        Native.KeyEncoderFree(_encoder);
    }
}
