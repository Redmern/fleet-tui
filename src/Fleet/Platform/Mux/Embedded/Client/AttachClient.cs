using System.Text;
using Fleet.Platform.Mux.Embedded.Host;
using Fleet.Platform.Mux.Embedded.Input;
using Fleet.Platform.Mux.Embedded.Native;
using Fleet.Platform.Mux.Embedded.Protocol;

namespace Fleet.Platform.Mux.Embedded.Client;

public sealed class AttachClient(Stream stream, string? workspace, Prefix prefix, Action<string> log, bool mouse = true)
{
    private const string EnterHost = "\e[?1049h\e[H\e[2J";

    private const string RestoreHost =
        "\e[?2026l\e[0m\e[?1000l\e[?1002l\e[?1003l\e[?1006l\e[?1004l\e[?2004l" +
        "\e[?1l\e>\e[0 q\e[?25h\e[?1049l";

    private const string PushTitle = "\e[22;0t";
    private const string PopTitle = "\e[23;0t";

    private readonly HashSet<ushort> _swallowed = [];
    private readonly FloatMode _floatMode = new();
    private volatile bool _running = true;
    private int _left;
    private string? _farewell;

    public async Task<int> RunAsync()
    {
        using var wire = new Wire(stream);

        if (OperatingSystem.IsWindows())
        {
            using var console = new WindowsConsole(mouse);
            var stdout = Console.OpenStandardOutput();
            return await RunAsync(wire, console.Size, bytes => { stdout.Write(bytes); stdout.Flush(); },
                () => WindowsInputAsync(wire, console), console.Dispose).ConfigureAwait(false);
        }

        using var terminal = new UnixTerminal();
        return await RunAsync(wire, terminal.Size, UnixOut.Write, () => UnixInputAsync(wire, terminal), terminal.Dispose)
            .ConfigureAwait(false);
    }

    private async Task<int> RunAsync(
        Wire wire, Func<(int Cols, int Rows)> size, Action<byte[]> write, Func<Task> input, Action release)
    {
        var (cols, rows) = size();

        await wire.SendAsync(
            MessageType.Hello,
            new Hello
            {
                Version = Wire.Version,
                Role = ClientRoles.Attach,
                Os = OperatingSystem.IsWindows() ? "windows" : "unix",
                Cols = cols,
                Rows = rows,
                Workspace = workspace,
            },
            WireJsonContext.Default.Hello).ConfigureAwait(false);

        if (await wire.ReceiveAsync().ConfigureAwait(false) is not { } welcome)
        {
            await Console.Error.WriteLineAsync("fleet: fleetd closed the connection").ConfigureAwait(false);
            return 1;
        }

        if (welcome.Type == MessageType.Error)
        {
            await Console.Error.WriteLineAsync(
                $"fleet: {Wire.Read(welcome.Payload, WireJsonContext.Default.ErrorMessage).Message}").ConfigureAwait(false);
            return 1;
        }

        var client = Wire.Read(welcome.Payload, WireJsonContext.Default.Welcome).Client;
        log($"attached as {client} at {cols}x{rows}");
        var savedTitle = HostEffectsOut.CurrentConsoleTitle();
        write(Encoding.UTF8.GetBytes(
            (OperatingSystem.IsWindows() ? string.Empty : PushTitle)
            + EnterHost
            + (mouse && !OperatingSystem.IsWindows() ? SgrMouse.Enable : string.Empty)));

        void Leave() => this.Leave(write, savedTitle, release);

        using var signals = HostSignals.OnExit(Leave);

        try
        {
            var reader = Task.Run(() => ReadFramesAsync(wire, write));
            var resizer = Task.Run(() => WatchSizeAsync(wire, size, (cols, rows)));
            var typing = Task.Run(input);

            var first = await Task.WhenAny(reader, typing).ConfigureAwait(false);
            log($"leaving: {(first == reader ? "frames ended" : "input ended")}, running={_running}, " +
                $"farewell={_farewell ?? "none"}, fault={first.Exception?.GetBaseException().Message ?? "none"}");
            _running = false;

            try
            {
                await wire.SendAsync(MessageType.Bye, ReadOnlyMemory<byte>.Empty).ConfigureAwait(false);
            }
            catch (Exception e) when (e is IOException or ObjectDisposedException)
            {
            }

            await resizer.ConfigureAwait(false);
        }
        finally
        {
            Leave();
        }

        if (_farewell is not null)
        {
            await Console.Out.WriteLineAsync($"fleet: {_farewell}").ConfigureAwait(false);
        }

        return 0;
    }

    public static string RestoreSequence => RestoreHost + (OperatingSystem.IsWindows() ? string.Empty : PopTitle);

    private void Leave(Action<byte[]> write, string? savedTitle, Action release)
    {
        if (Interlocked.Exchange(ref _left, 1) != 0)
        {
            return;
        }

        try
        {
            write(Encoding.UTF8.GetBytes(RestoreSequence));
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException)
        {
        }

        if (savedTitle is not null)
        {
            HostEffectsOut.SetConsoleTitle(savedTitle);
        }

        release();
    }

    private void Apply(HostEffect effect, Action<byte[]> write)
    {
        switch (effect.Kind)
        {
            case HostEffects.Title when effect.Value is { } title:
                write(Encoding.UTF8.GetBytes(HostEffectsOut.TitleSequence(title)));
                break;

            case HostEffects.Clipboard when effect.Value is { } text:
                var done = OperatingSystem.IsWindows()
                    ? HostEffectsOut.SetWindowsClipboard(text)
                    : WriteAndReport(write, HostEffectsOut.ClipboardSequence(text));
                log($"clipboard: {text.Length} chars {(done ? "set" : "NOT set")}");
                break;
        }
    }

    private static bool WriteAndReport(Action<byte[]> write, string sequence)
    {
        write(Encoding.UTF8.GetBytes(sequence));
        return true;
    }

    private async Task ReadFramesAsync(Wire wire, Action<byte[]> write)
    {
        try
        {
            while (_running && await wire.ReceiveAsync().ConfigureAwait(false) is { } message)
            {
                switch (message.Type)
                {
                    case MessageType.Frame:
                        write(Wire.ReadFrame(message.Payload).Bytes);
                        break;
                    case MessageType.HostEffect:
                        Apply(Wire.Read(message.Payload, WireJsonContext.Default.HostEffect), write);
                        break;
                    case MessageType.Bye:
                        _farewell = "fleetd ended the session";
                        return;
                }
            }

            _farewell ??= _running ? "fleetd went away" : null;
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or EndOfStreamException)
        {
            _farewell ??= _running ? $"lost fleetd: {e.Message}" : null;
        }
    }

    private async Task WatchSizeAsync(Wire wire, Func<(int Cols, int Rows)> size, (int Cols, int Rows) last)
    {
        while (_running)
        {
            await Task.Delay(100).ConfigureAwait(false);
            var now = size();

            if (now == last || now.Cols < 2 || now.Rows < 2)
            {
                continue;
            }

            last = now;
            await Send(wire, MessageType.Resize, new ResizeMessage { Cols = now.Cols, Rows = now.Rows },
                WireJsonContext.Default.ResizeMessage).ConfigureAwait(false);
        }
    }

    private async Task WindowsInputAsync(Wire wire, WindowsConsole console)
    {
        var keys = new WindowsKeys();
        var pointer = new WindowsMouse();
        var records = new WindowsConsole.InputRecord[512];

        while (_running)
        {
            var n = console.Read(records, 50);
            if (n < 0)
            {
                log($"console input failed, error {System.Runtime.InteropServices.Marshal.GetLastPInvokeError()}");
                return;
            }

            IReadOnlyList<WindowsConsole.InputRecord> batch = records[..n];

            if (!prefix.Armed && !_floatMode.Active && PasteBurst.Starts(records.AsSpan(0, n)))
            {
                var burst = new List<WindowsConsole.InputRecord>(batch);
                int more;
                while ((more = console.Read(records, PasteBurst.QuietMs)) > 0)
                {
                    burst.AddRange(records[..more]);
                }

                if (PasteBurst.Paste(burst) is { } pasted)
                {
                    log($"paste: {pasted.Length} chars in {burst.Count} records");
                    await Send(wire, MessageType.Text, new TextMessage { Text = pasted, Paste = true },
                        WireJsonContext.Default.TextMessage).ConfigureAwait(false);
                    continue;
                }

                batch = burst;
            }

            for (var i = 0; i < batch.Count && _running; i++)
            {
                switch (batch[i].EventType)
                {
                    case WindowsConsole.KeyEvent:
                        await OnKeyRecordAsync(wire, keys, batch[i].Key).ConfigureAwait(false);
                        break;
                    case WindowsConsole.FocusEvent:
                        await Command(wire, batch[i].SetFocus != 0 ? "focus-in" : "focus-out").ConfigureAwait(false);
                        break;
                    case WindowsConsole.MouseEvent when mouse:
                    {
                        var (left, top) = console.WindowOrigin();
                        foreach (var message in pointer.Translate(batch[i].Mouse, left, top))
                        {
                            await Send(wire, MessageType.Mouse, message, WireJsonContext.Default.MouseMessage)
                                .ConfigureAwait(false);
                        }

                        break;
                    }
                }
            }
        }
    }

    private async Task OnKeyRecordAsync(Wire wire, WindowsKeys keys, WindowsConsole.KeyEventRecord record)
    {
        var inputs = keys.Translate(record).ToList();
        var win32 = new Win32Key
        {
            Vk = record.VirtualKeyCode,
            Sc = record.VirtualScanCode,
            Uc = record.UnicodeChar,
            Down = record.KeyDown != 0,
            State = record.ControlKeyState,
            Repeat = record.RepeatCount,
        };

        if (_floatMode.Active)
        {
            if (inputs.Count > 0 && inputs[0] is { IsRawText: false } step)
            {
                if (step.Action == KeyAction.Release)
                {
                    _swallowed.Remove(step.VirtualKey);
                }
                else
                {
                    _swallowed.Add(step.VirtualKey);
                    await FloatStep(wire, _floatMode.OnKey(step.Key, step.Mods)).ConfigureAwait(false);
                }
            }

            return;
        }

        if (inputs.Count == 0)
        {
            await Send(wire, MessageType.Key, new KeyMessage { Action = 0, Win32 = win32 },
                WireJsonContext.Default.KeyMessage).ConfigureAwait(false);
            return;
        }

        var input = inputs[0];

        if (input.IsRawText)
        {
            await Send(wire, MessageType.Text, new TextMessage { Text = input.Utf8 },
                WireJsonContext.Default.TextMessage).ConfigureAwait(false);
            return;
        }

        if (input.Action == KeyAction.Release && _swallowed.Remove(input.VirtualKey))
        {
            return;
        }

        var command = input.Action == KeyAction.Release ? PrefixCommand.None : prefix.OnKey(input.Key, input.Mods);

        switch (command)
        {
            case PrefixCommand.Armed:
                _swallowed.Add(input.VirtualKey);
                await Badge(wire, prefix.Label).ConfigureAwait(false);
                return;
            case PrefixCommand.Chord:
                _swallowed.Add(input.VirtualKey);
                await Badge(wire, null).ConfigureAwait(false);
                await Chord(wire, Prefix.CommandFor(input.Key)).ConfigureAwait(false);
                return;
            case PrefixCommand.SendPrefix:
                await Badge(wire, null).ConfigureAwait(false);
                break;
        }

        await Send(
            wire,
            MessageType.Key,
            new KeyMessage
            {
                Key = (int)input.Key,
                Mods = (int)input.Mods,
                Consumed = (int)input.Consumed,
                Text = input.Utf8,
                Action = (int)input.Action,
                Unshifted = input.Unshifted,
                Repeat = inputs.Count,
                Win32 = win32,
            },
            WireJsonContext.Default.KeyMessage).ConfigureAwait(false);
    }

    private async Task UnixInputAsync(Wire wire, UnixTerminal terminal)
    {
        var buffer = new byte[4096];
        var pending = new List<byte>(4096);
        var sgr = new SgrMouse();

        while (_running)
        {
            var n = await Task.Run(() => terminal.Read(buffer)).ConfigureAwait(false);
            if (n <= 0)
            {
                return;
            }

            var items = mouse ? sgr.Feed(buffer.AsSpan(0, n)) : [buffer[..n]];

            foreach (var item in items)
            {
                if (item is MouseMessage message)
                {
                    await Send(wire, MessageType.Mouse, message, WireJsonContext.Default.MouseMessage).ConfigureAwait(false);
                    continue;
                }

                await TypedBytesAsync(wire, (byte[])item, pending).ConfigureAwait(false);
            }
        }
    }

    private async Task TypedBytesAsync(Wire wire, byte[] bytes, List<byte> pending)
    {
        pending.Clear();
        for (var i = 0; i < bytes.Length; i++)
        {
            var b = bytes[i];

            if (_floatMode.Active)
            {
                await Flush(wire, pending).ConfigureAwait(false);
                i += _floatMode.OnBytes(bytes.AsSpan(i), out var step) - 1;
                await FloatStep(wire, step).ConfigureAwait(false);
                continue;
            }

            var command = prefix.OnByte(b);
            if (command == PrefixCommand.None)
            {
                pending.Add(b);
                continue;
            }

            await Flush(wire, pending).ConfigureAwait(false);

            switch (command)
            {
                case PrefixCommand.Armed:
                    await Badge(wire, prefix.Label).ConfigureAwait(false);
                    break;
                case PrefixCommand.SendPrefix:
                    await Badge(wire, null).ConfigureAwait(false);
                    pending.Add(prefix.ControlByte);
                    break;
                case PrefixCommand.Chord:
                    await Badge(wire, null).ConfigureAwait(false);
                    await Chord(wire, Prefix.CommandFor(b)).ConfigureAwait(false);
                    break;
            }
        }

        await Flush(wire, pending).ConfigureAwait(false);
    }

    private async Task Flush(Wire wire, List<byte> pending)
    {
        if (pending.Count == 0)
        {
            return;
        }

        await Send(wire, MessageType.Text, new TextMessage { Bytes = Convert.ToBase64String([.. pending]) },
            WireJsonContext.Default.TextMessage).ConfigureAwait(false);
        pending.Clear();
    }

    private async Task Chord(Wire wire, string? command)
    {
        if (command is null)
        {
            return;
        }

        if (command == "detach")
        {
            _running = false;
            return;
        }

        if (command == "float-mode")
        {
            _floatMode.Enter();
            await Badge(wire, FloatMode.Badge).ConfigureAwait(false);
            return;
        }

        await Command(wire, command).ConfigureAwait(false);
    }

    private async Task FloatStep(Wire wire, CommandMessage? step)
    {
        if (step is not null)
        {
            await Send(wire, MessageType.Command, step, WireJsonContext.Default.CommandMessage).ConfigureAwait(false);
        }

        if (!_floatMode.Active)
        {
            await Badge(wire, null).ConfigureAwait(false);
        }
    }

    private Task Command(Wire wire, string name) =>
        Send(wire, MessageType.Command, new CommandMessage { Name = name }, WireJsonContext.Default.CommandMessage);

    private Task Badge(Wire wire, string? text) =>
        Send(wire, MessageType.Badge, new BadgeMessage { Text = text }, WireJsonContext.Default.BadgeMessage);

    private async Task Send<T>(Wire wire, MessageType type, T message,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> info)
    {
        try
        {
            await wire.SendAsync(type, message, info).ConfigureAwait(false);
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException)
        {
            _running = false;
        }
    }
}
