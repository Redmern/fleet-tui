using Fleet.Platform.Mux.Embedded.Protocol;
using Fleet.Platform.Mux.Embedded.Render;

namespace Fleet.Platform.Mux.Embedded.Native;

public interface IPaneTerminal : IDisposable
{
    void Write(ReadOnlySpan<byte> data);

    void Resize(int cols, int rows);

    bool Snapshot(ScreenBuffer screen);

    byte[] Encode(KeyMessage key);

    byte[] EncodeMouse(MouseMessage mouse, int x, int y);

    string PlainText();

    string Title { get; }

    event Action? TitleChanged;

    event Action<string>? Copied;
}

public delegate IPaneTerminal PaneTerminalFactory(int cols, int rows, Action<byte[]> reply);
