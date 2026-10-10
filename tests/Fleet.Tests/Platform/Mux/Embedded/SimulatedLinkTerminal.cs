using Fleet.Platform.Mux.Embedded.Native;
using Fleet.Platform.Mux.Embedded.Protocol;
using Fleet.Platform.Mux.Embedded.Render;

namespace Fleet.Tests.Platform.Mux.Embedded;

// The home-side terminal for a remote pane in simulated-link tests. A remote pane is fed the
// far fleetd's encoded frames; this drops the escape sequences and turns each cursor move
// into a new line, so the far screen's text shows up line by line in a FakeTerminal.
public sealed class SimulatedLinkTerminal(FakePanes.FakeTerminal inner) : IPaneTerminal
{
    private enum State
    {
        Text,
        Escape,
        Csi,
        Osc,
        OscEscape,
    }

    private State _state = State.Text;

    public static PaneTerminalFactory Over(FakePanes panes) =>
        (cols, rows, reply) => new SimulatedLinkTerminal((FakePanes.FakeTerminal)panes.NewTerminal(cols, rows, reply));

    public void Write(ReadOnlySpan<byte> data)
    {
        var text = new List<byte>(data.Length);
        foreach (var b in data)
        {
            switch (_state)
            {
                case State.Text when b == 0x1b:
                    _state = State.Escape;
                    break;
                case State.Text when b != '\r':
                    text.Add(b);
                    break;
                case State.Escape:
                    _state = b switch
                    {
                        (byte)'[' => State.Csi,
                        (byte)']' => State.Osc,
                        _ => State.Text,
                    };
                    break;
                case State.Csi when b is >= 0x40 and <= 0x7e:
                    if (b == 'H')
                    {
                        text.Add((byte)'\n');
                    }

                    _state = State.Text;
                    break;
                case State.Osc when b == 0x07:
                    _state = State.Text;
                    break;
                case State.Osc when b == 0x1b:
                    _state = State.OscEscape;
                    break;
                case State.OscEscape:
                    _state = State.Text;
                    break;
            }
        }

        inner.Write(text.ToArray());
    }

    public void Resize(int cols, int rows) => inner.Resize(cols, rows);

    public bool Snapshot(ScreenBuffer screen) => inner.Snapshot(screen);

    public byte[] Encode(KeyMessage key) => inner.Encode(key);

    public byte[] EncodeMouse(MouseMessage mouse, int x, int y) => inner.EncodeMouse(mouse, x, y);

    public string PlainText() => inner.PlainText();

    public Viewport Viewport => inner.Viewport;

    public void Scroll(ScrollTo target, long value = 0) => inner.Scroll(target, value);

    public string Text(TextPoint from, TextPoint to) => inner.Text(from, to);

    public string Title => inner.Title;

    public event Action? TitleChanged
    {
        add => inner.TitleChanged += value;
        remove => inner.TitleChanged -= value;
    }

    public event Action<string>? Copied
    {
        add => inner.Copied += value;
        remove => inner.Copied -= value;
    }

    public void Dispose() => inner.Dispose();
}
