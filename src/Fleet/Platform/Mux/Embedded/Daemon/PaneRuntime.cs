using System.Threading.Channels;
using Fleet.Platform.Mux.Embedded.Input;
using Fleet.Platform.Mux.Embedded.Native;
using Fleet.Platform.Mux.Embedded.Pty;
using Fleet.Platform.Mux.Embedded.Render;

namespace Fleet.Platform.Mux.Embedded.Daemon;

public sealed class PaneRuntime : IDisposable
{
    private readonly Channel<byte[]> _toPty = Channel.CreateUnbounded<byte[]>(
        new UnboundedChannelOptions { SingleReader = true });

    private readonly Task _writer;

    private readonly SizeQueries _sizeQueries = new();

    private (int Cols, int Rows) _size;

    public PaneRuntime(string id, IPanePty pty, PaneTerminalFactory terminals, int cols, int rows)
    {
        Id = id;
        Pty = pty;
        Terminal = terminals(cols, rows, Reply);
        Screen.Resize(cols, rows);
        _size = (cols, rows);
        _writer = Task.Run(WriteLoopAsync);
    }

    public string Id { get; }

    public IPanePty Pty { get; }

    public IPaneTerminal Terminal { get; }

    public ScreenBuffer Screen { get; } = new();

    public ConPtyModes Modes { get; } = new();

    public Lock Gate { get; } = new();

    public bool Dirty { get; set; } = true;

    public volatile bool TitleDirty;

    public bool Exited { get; set; }

    public long Outputs { get; private set; }

    public long LastOutputAt { get; private set; }

    public void Reply(byte[] bytes) => Send(Modes.Win32Input ? KittyReplies.Without(bytes) : bytes);

    public void Send(byte[] bytes)
    {
        if (bytes.Length > 0)
        {
            _toPty.Writer.TryWrite(bytes);
        }
    }

    public string ModeSummary =>
        $"win32-input={Modes.Win32Input} bracketed-paste={Modes.BracketedPaste} focus={Modes.FocusEvents}";

    public bool Feed(byte[] buffer, int count)
    {
        lock (Gate)
        {
            var before = ModeSummary;
            Terminal.Write(buffer.AsSpan(0, count));
            Modes.Feed(buffer.AsSpan(0, count));
            Dirty = true;
            Outputs++;
            LastOutputAt = Environment.TickCount64;

            for (var asked = _sizeQueries.Count(buffer.AsSpan(0, count)); asked > 0 && !Modes.Win32Input; asked--)
            {
                Send(SizeQueries.Reply(_size.Cols, _size.Rows));
            }

            return before != ModeSummary;
        }
    }

    public void Resize(int cols, int rows)
    {
        lock (Gate)
        {
            Terminal.Resize(cols, rows);
            _size = (cols, rows);
            Dirty = true;
        }

        try
        {
            Pty.Resize(cols, rows);
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or ObjectDisposedException)
        {
        }
    }

    public bool Snapshot()
    {
        lock (Gate)
        {
            if (!Dirty)
            {
                return false;
            }

            Dirty = false;
            return Terminal.Snapshot(Screen);
        }
    }

    public void Dispose()
    {
        _toPty.Writer.TryComplete();

        try
        {
            Pty.Dispose();
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or ObjectDisposedException)
        {
        }

        lock (Gate)
        {
            Terminal.Dispose();
        }
    }

    private async Task WriteLoopAsync()
    {
        await foreach (var bytes in _toPty.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                Pty.Write(bytes);
            }
            catch (Exception e) when (e is IOException or InvalidOperationException or ObjectDisposedException)
            {
                return;
            }
        }
    }
}
