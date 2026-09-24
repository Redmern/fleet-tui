using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using EmbeddedSpike.Ghostty;
using EmbeddedSpike.Host;
using EmbeddedSpike.Input;
using EmbeddedSpike.Pty;
using EmbeddedSpike.Render;

namespace EmbeddedSpike;

// One pane, fullscreen. Four threads:
//   PTY reader   -> feeds the emulator under Gate, wakes the renderer
//   renderer     -> diffs the grid onto the host, checks the host size
//   PTY writer   -> the only thread that writes to the PTY
//   input (main) -> host keys -> prefix check -> key encoder -> PTY writer
internal sealed class Session(Options options)
{
    private const string EnterHost = "\e[?1049h\e[H\e[2J";

    // Everything a pane program may have switched on in the host terminal, or
    // that the spike itself set, switched back off.
    private const string RestoreHost =
        "\e[?2026l\e[0m\e[?1000l\e[?1002l\e[?1003l\e[?1006l\e[?1004l\e[?2004l" +
        "\e[?1l\e>\e[0 q\e[?25h\e[?1049l";

    private readonly BlockingCollection<byte[]> _toPty = new();
    private readonly AutoResetEvent _wake = new(false);
    private readonly Prefix _prefix = Prefix.Parse(options.Prefix);
    private readonly HashSet<ushort> _swallowed = [];
    private readonly ConPtyModes _modes = new();
    private readonly Lock _logGate = new();
    private readonly Lock _dumpGate = new();

    private volatile bool _running = true;
    private int _childExit = -1;
    private long _ptyBytes;
    private long _dumpedFrames = -1;
    private DateTime _lastDump = DateTime.MinValue;

    private VtTerminal _terminal = null!;
    private GridRenderer _renderer = null!;
    private KeyEncoder _encoder = null!;
    private Action<byte[]> _writeHost = null!;
    private Func<(int Cols, int Rows)> _hostSize = null!;
    private (int Cols, int Rows) _size;

    public int Run()
    {
        using var log = options.LogPath is null ? null : new StreamWriter(options.LogPath, false, Encoding.UTF8) { AutoFlush = true };
        _log = log;

        return OperatingSystem.IsWindows() ? RunWindows() : RunUnix();
    }

    private StreamWriter? _log;

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private int RunWindows()
    {
        using var console = new WindowsConsole();
        var stdout = Console.OpenStandardOutput();
        _writeHost = bytes =>
        {
            stdout.Write(bytes);
            stdout.Flush();
        };
        _hostSize = console.Size;
        Log($"host: {console.Describe()}");

        using var pty = IPanePty.Create();
        try
        {
            Start(pty);

            var keys = new WindowsKeys();
            var records = new WindowsConsole.InputRecord[64];

            while (_running)
            {
                var n = console.Read(records, 50);
                if (n < 0)
                {
                    break;
                }

                for (var i = 0; i < n && _running; i++)
                {
                    switch (records[i].EventType)
                    {
                        case WindowsConsole.KeyEvent:
                            OnKeyRecord(keys, records[i].Key);
                            break;
                        case WindowsConsole.WindowBufferSizeEvent:
                            _wake.Set();
                            break;
                        case WindowsConsole.FocusEvent when _modes.FocusEvents:
                            _toPty.Add(records[i].SetFocus != 0 ? "\e[I"u8.ToArray() : "\e[O"u8.ToArray());
                            break;
                    }
                }
            }
        }
        finally
        {
            Stop();
        }

        console.Dispose();
        return Report();
    }

    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    private int RunUnix()
    {
        using var terminal = new UnixTerminal();
        _writeHost = UnixOut.Write;
        _hostSize = terminal.Size;
        Log($"host: {terminal.Describe()}");

        using var winch = PosixSignalRegistration.Create(PosixSignal.SIGWINCH, ctx =>
        {
            ctx.Cancel = true;
            _wake.Set();
        });

        using var pty = IPanePty.Create();
        try
        {
            Start(pty);

            var input = new Thread(() => UnixInputLoop(terminal)) { IsBackground = true, Name = "input" };
            input.Start();

            while (_running)
            {
                Thread.Sleep(50);
            }
        }
        finally
        {
            Stop();
        }

        terminal.Dispose();
        return Report();
    }

    private Thread? _render;
    private Thread? _writer;
    private IPanePty? _pty;

    private void Start(IPanePty pty)
    {
        _pty = pty;
        _size = _hostSize();
        _writeHost(Encoding.UTF8.GetBytes(EnterHost));

        _terminal = new VtTerminal((ushort)_size.Cols, (ushort)_size.Rows, bytes => _toPty.Add(bytes.ToArray()));
        _renderer = new GridRenderer(_terminal);
        _encoder = new KeyEncoder(_terminal);

        pty.Output += (buffer, count) =>
        {
            lock (_terminal.Gate)
            {
                _terminal.Write(buffer.AsSpan(0, count));
            }

            _modes.Feed(buffer.AsSpan(0, count));

            if (Interlocked.Add(ref _ptyBytes, count) == count)
            {
                Log($"first pty bytes [{Hex(buffer.AsSpan(0, Math.Min(count, 48)).ToArray())}]");
            }
            _wake.Set();
        };

        pty.Exited += code =>
        {
            _childExit = code;
            _running = false;
            _wake.Set();
        };

        _writer = new Thread(WriterLoop) { IsBackground = true, Name = "pty-writer" };
        _writer.Start();

        _render = new Thread(RenderLoop) { IsBackground = true, Name = "render" };
        _render.Start();

        Log($"start {options.Program} {string.Join(' ', options.Args)} at {_size.Cols}x{_size.Rows}");
        pty.Start(options.Program, options.Args, _size.Cols, _size.Rows);
    }

    private void Stop()
    {
        _running = false;
        _wake.Set();
        _render?.Join(1000);
        _toPty.CompleteAdding();

        if (_terminal is not null)
        {
            Dump(force: true);
            _writeHost(Encoding.UTF8.GetBytes(RestoreHost));
        }
    }

    private int Report()
    {
        var line = $"embeddedspike: {(_childExit >= 0 ? $"child exited with {_childExit}" : "quit by prefix")}, " +
                   $"{_ptyBytes} bytes from the pty, {_renderer?.Frames} frames, {_renderer?.CellsWritten} cells written";
        Log(line);
        _writeHost(Encoding.UTF8.GetBytes(line + "\r\n"));
        return _childExit >= 0 ? _childExit : 0;
    }

    private void WriterLoop()
    {
        foreach (var bytes in _toPty.GetConsumingEnumerable())
        {
            try
            {
                _pty!.Write(bytes);
            }
            catch (Exception e) when (e is IOException or ObjectDisposedException or InvalidOperationException)
            {
                Log($"pty write failed: {e.Message}");
            }
        }
    }

    private void RenderLoop()
    {
        try
        {
            RenderFrames();
        }
        catch (Exception e)
        {
            Log($"render thread failed: {e}");
            _running = false;
        }
    }

    private void RenderFrames()
    {
        while (_running)
        {
            _wake.WaitOne(100);
            if (!_running)
            {
                break;
            }

            Thread.Sleep(4);
            CheckResize();

            string frame;
            lock (_terminal.Gate)
            {
                _renderer.SetBadge(_prefix.Armed ? $" {PrefixLabel()} " : null);
                frame = _renderer.Frame();
            }

            _writeHost(Encoding.UTF8.GetBytes(frame));
            Dump(force: false);
        }
    }

    private void CheckResize()
    {
        var size = _hostSize();
        if (size == _size || size.Cols < 2 || size.Rows < 2)
        {
            return;
        }

        _size = size;
        lock (_terminal.Gate)
        {
            _terminal.Resize((ushort)size.Cols, (ushort)size.Rows);
            _renderer.Invalidate();
        }

        _pty!.Resize(size.Cols, size.Rows);
        Log($"resize {size.Cols}x{size.Rows}");
    }

    // Prefix decisions are made on the translated key. What reaches the pane is,
    // when ConPTY has asked for win32-input-mode (it always does on current
    // Windows), the record itself; otherwise libghostty-vt's encoding of the key
    // under the pane's current modes. --keys ghostty forces the latter.
    private void OnKeyRecord(WindowsKeys keys, WindowsConsole.KeyEventRecord record)
    {
        var raw = options.Keys != "ghostty" && _modes.Win32Input;
        var inputs = keys.Translate(record).ToList();

        if (inputs.Count == 0)
        {
            if (raw)
            {
                _toPty.Add(ConPtyModes.Encode(record));
            }

            return;
        }

        var input = inputs[0];

        if (input.Action == KeyAction.Release && _swallowed.Remove(input.VirtualKey))
        {
            return;
        }

        var command = input.Action == KeyAction.Release || input.IsRawText
            ? PrefixCommand.None
            : _prefix.OnKey(input.Key, input.Mods);

        if (command is not (PrefixCommand.None or PrefixCommand.SendPrefix))
        {
            _swallowed.Add(input.VirtualKey);
        }

        var bytes = new List<byte>();
        if (command is PrefixCommand.None or PrefixCommand.SendPrefix)
        {
            if (raw)
            {
                bytes.AddRange(ConPtyModes.Encode(record));
            }
            else
            {
                lock (_terminal.Gate)
                {
                    foreach (var each in inputs)
                    {
                        bytes.AddRange(_encoder.Encode(each));
                    }
                }
            }
        }

        Log($"key vk=0x{record.VirtualKeyCode:X2} ch=U+{(int)record.UnicodeChar:X4} state=0x{record.ControlKeyState:X} " +
            $"down={record.KeyDown} -> {input.Key} {input.Mods} {input.Action} [{Hex([.. bytes])}] {command}{(raw ? " win32" : string.Empty)}");

        Apply(command, [.. bytes]);
    }

    private void UnixInputLoop(UnixTerminal terminal)
    {
        var buffer = new byte[4096];
        var pending = new List<byte>(4096);

        while (_running)
        {
            var n = terminal.Read(buffer);
            if (n <= 0)
            {
                break;
            }

            pending.Clear();
            for (var i = 0; i < n; i++)
            {
                var command = _prefix.OnByte(buffer[i]);
                if (command == PrefixCommand.None)
                {
                    pending.Add(buffer[i]);
                    continue;
                }

                if (pending.Count > 0)
                {
                    _toPty.Add([.. pending]);
                    pending.Clear();
                }

                Apply(command, command == PrefixCommand.SendPrefix ? [_prefix.ControlByte] : []);
            }

            if (pending.Count > 0)
            {
                Log($"stdin [{Hex([.. pending])}]");
                _toPty.Add([.. pending]);
            }
        }
    }

    private void Apply(PrefixCommand command, byte[] bytes)
    {
        switch (command)
        {
            case PrefixCommand.None:
            case PrefixCommand.SendPrefix:
                if (bytes.Length > 0)
                {
                    _toPty.Add(bytes);
                }

                break;
            case PrefixCommand.Quit:
                _running = false;
                break;
            case PrefixCommand.Dump:
                Dump(force: true);
                break;
            case PrefixCommand.Redraw:
                lock (_terminal.Gate)
                {
                    _renderer.Invalidate();
                }

                break;
        }

        _wake.Set();
    }

    private string PrefixLabel() => $"ctrl+{_prefix.Letter}";

    private void Dump(bool force)
    {
        if (options.DumpPath is null)
        {
            return;
        }

        lock (_dumpGate)
        {
            DumpLocked(force);
        }
    }

    private void DumpLocked(bool force)
    {

        var frames = _renderer.Frames;
        if (!force && (frames == _dumpedFrames || DateTime.UtcNow - _lastDump < TimeSpan.FromMilliseconds(250)))
        {
            return;
        }

        string text;
        bool alt;
        lock (_terminal.Gate)
        {
            text = _terminal.PlainText();
            alt = _terminal.AlternateScreen;
        }

        var header =
            $"size {_terminal.Cols}x{_terminal.Rows}\n" +
            $"alt-screen {alt}\n" +
            $"pty-bytes {Interlocked.Read(ref _ptyBytes)}\n" +
            $"frames {frames}\n" +
            $"cells-written {_renderer.CellsWritten}\n" +
            $"prefix-armed {_prefix.Armed}\n" +
            "---\n";

        var temp = options.DumpPath + ".tmp";
        File.WriteAllText(temp, header + text, Encoding.UTF8);
        File.Move(temp, options.DumpPath!, overwrite: true);
        _dumpedFrames = frames;
        _lastDump = DateTime.UtcNow;
    }

    private void Log(string line)
    {
        lock (_logGate)
        {
            _log?.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {line}");
        }
    }

    private static string Hex(byte[] bytes) => string.Join(' ', bytes.Select(b => b.ToString("X2")));
}
