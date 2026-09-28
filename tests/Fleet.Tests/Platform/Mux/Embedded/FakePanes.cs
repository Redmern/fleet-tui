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
        private long _top;
        private bool _following = true;

        public bool MouseTracking { get; set; } = true;

        public bool AltScreen { get; set; }

        public List<(ScrollTo Target, long Value)> Scrolls { get; } = [];

        private string[] Lines => _text.ToString().Split('\n');

        public Viewport Viewport
        {
            get
            {
                var total = Math.Max(Lines.Length, _size.Rows);
                var bottom = total - _size.Rows;
                var top = _following ? bottom : Math.Clamp(_top, 0, bottom);
                return new Viewport(top, total, _size.Rows, top == bottom, AltScreen);
            }
        }

        public void Scroll(ScrollTo target, long value = 0)
        {
            Scrolls.Add((target, value));
            var now = Viewport;
            _top = target switch
            {
                ScrollTo.Top => 0,
                ScrollTo.Delta => now.Top + value,
                ScrollTo.Row => value,
                _ => now.Total,
            };
            _following = target == ScrollTo.Bottom || _top >= now.Total - now.Rows;
        }

        public string Text(TextPoint from, TextPoint to)
        {
            if (to.CompareTo(from) < 0)
            {
                (from, to) = (to, from);
            }

            var lines = Lines;
            var picked = new List<string>();
            for (var row = from.Row; row <= to.Row && row < lines.Length; row++)
            {
                var line = lines[row].PadRight(_size.Cols);
                var first = row == from.Row ? from.Col : 0;
                var last = row == to.Row ? to.Col : _size.Cols - 1;
                picked.Add(line[first..Math.Min(line.Length, last + 1)].TrimEnd());
            }

            return string.Join('\n', picked);
        }

        public void Write(ReadOnlySpan<byte> data) => _text.Append(Encoding.UTF8.GetString(data));

        public void Resize(int cols, int rows) => _size = (cols, rows);

        public bool Snapshot(ScreenBuffer screen)
        {
            screen.Resize(_size.Cols, _size.Rows);
            var viewport = Viewport;
            var lines = Lines;

            for (var y = 0; y < _size.Rows; y++)
            {
                var index = viewport.Top + y;
                var line = index < lines.Length ? lines[index] : string.Empty;
                screen.Write(0, y, line.PadRight(_size.Cols)[.._size.Cols]);
            }

            screen.CursorX = Math.Min(lines[^1].Length, _size.Cols - 1);
            screen.CursorY = (int)Math.Clamp(lines.Length - 1 - viewport.Top, 0, _size.Rows - 1);
            screen.Viewport = viewport;
            return true;
        }

        public byte[] Encode(KeyMessage key) => Encoding.UTF8.GetBytes(key.Text ?? $"<key {(Key)key.Key}>");

        public byte[] EncodeMouse(MouseMessage mouse, int x, int y) =>
            MouseTracking ? Encoding.UTF8.GetBytes($"<mouse b{mouse.Button} a{mouse.Action} {x},{y}>") : [];

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
