using System.Collections.Concurrent;
using System.Text;
using Fleet.Platform.Mux.Embedded.Native;
using Fleet.Platform.Mux.Embedded.Protocol;
using Fleet.Platform.Mux.Embedded.Pty;
using Fleet.Platform.Mux.Embedded.Render;

namespace Fleet.Tests.Platform.Mux.Embedded;

public sealed class FakePanes
{
    public ConcurrentQueue<FakePty> Started { get; } = new();

    public int Disposed => Started.Count(p => p.IsDisposed);

    public FakePty? ByProgram(string program) => Started.LastOrDefault(p => p.Program == program);

    public IPanePty NewPty() => new FakePty(this);

    public ConcurrentQueue<FakeTerminal> Terminals { get; } = new();

    public IPaneTerminal NewTerminal(int cols, int rows, Action<byte[]> reply)
    {
        var terminal = new FakeTerminal(cols, rows);
        Terminals.Enqueue(terminal);
        return terminal;
    }

    public sealed class FakePty(FakePanes owner) : IPanePty
    {
        private readonly ConcurrentQueue<byte[]> _written = new();

        public event Action<byte[], int>? Output;

        public event Action<int>? Exited;

        public string Program { get; private set; } = string.Empty;

        public IReadOnlyList<string> Args { get; private set; } = [];

        public IReadOnlyDictionary<string, string> Env { get; private set; } = new Dictionary<string, string>();

        public (int Cols, int Rows) Size { get; private set; }

        public bool IsDisposed { get; private set; }

        public FakeTerminal Terminal { get; private set; } = null!;

        public string Written => string.Concat(_written.Select(b => Encoding.UTF8.GetString(b)));

        public void Start(
            string program, IReadOnlyList<string> args, int cols, int rows, string cwd,
            IReadOnlyDictionary<string, string> env)
        {
            Program = program;
            Args = args;
            Env = env;
            Size = (cols, rows);
            Terminal = owner.Terminals.Last();
            owner.Started.Enqueue(this);
        }

        public void Emit(string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            Output?.Invoke(bytes, bytes.Length);
        }

        public void Exit() => Exited?.Invoke(0);

        public void Write(ReadOnlySpan<byte> data) => _written.Enqueue(data.ToArray());

        public void Resize(int cols, int rows) => Size = (cols, rows);

        public void Dispose() => IsDisposed = true;
    }

    public sealed class FakeTerminal(int cols, int rows) : IPaneTerminal
    {
        private readonly StringBuilder _text = new();
        private (int Cols, int Rows) _size = (cols, rows);

        public void Write(ReadOnlySpan<byte> data) => _text.Append(Encoding.UTF8.GetString(data));

        public void Resize(int cols, int rows) => _size = (cols, rows);

        public bool Snapshot(ScreenBuffer screen)
        {
            screen.Resize(_size.Cols, _size.Rows);
            var line = _text.ToString().Split('\n').Last();
            screen.Write(0, 0, line.PadRight(_size.Cols)[.._size.Cols]);
            screen.CursorX = Math.Min(line.Length, _size.Cols - 1);
            return true;
        }

        public byte[] Encode(KeyMessage key) => Encoding.UTF8.GetBytes(key.Text ?? string.Empty);

        public byte[] EncodeMouse(MouseMessage mouse, int x, int y) =>
            Encoding.UTF8.GetBytes($"<mouse b{mouse.Button} a{mouse.Action} {x},{y}>");

        public string Title { get; private set; } = string.Empty;

        public event Action? TitleChanged;

        public event Action<string>? Copied;

        public void SetTitle(string title)
        {
            Title = title;
            TitleChanged?.Invoke();
        }

        public void Copy(string text) => Copied?.Invoke(text);

        public string PlainText() => _text.ToString();

        public void Dispose()
        {
        }
    }
}
