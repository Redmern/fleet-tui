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

    Viewport Viewport { get; }

    void Scroll(ScrollTo target, long value = 0);

    string Text(TextPoint from, TextPoint to);

    string Title { get; }

    event Action? TitleChanged;

    event Action<string>? Copied;
}

public delegate IPaneTerminal PaneTerminalFactory(int cols, int rows, Action<byte[]> reply);

public enum ScrollTo
{
    Top = 0,
    Bottom = 1,
    Delta = 2,
    Row = 3,
}
