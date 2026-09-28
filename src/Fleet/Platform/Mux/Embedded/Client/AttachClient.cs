using System.Text;
using Fleet.Platform.Mux.Embedded.Host;
using Fleet.Platform.Mux.Embedded.Input;
using Fleet.Platform.Mux.Embedded.Native;
using Fleet.Platform.Mux.Embedded.Protocol;

namespace Fleet.Platform.Mux.Embedded.Client;

public sealed class AttachClient(Stream stream, string? workspace, Prefix prefix, Action<string> log)
{
    private const string EnterHost = "\e[?1049h\e[H\e[2J";

    private const string RestoreHost =
        "\e[?2026l\e[0m\e[?1000l\e[?1002l\e[?1003l\e[?1006l\e[?1004l\e[?2004l" +
        "\e[?1l\e>\e[0 q\e[?25h\e[?1049l";

    private readonly HashSet<ushort> _swallowed = [];
    private volatile bool _running = true;
    private string? _farewell;

    public async Task<int> RunAsync()
    {
        using var wire = new Wire(stream);

        if (OperatingSystem.IsWindows())
        {
            using var console = new WindowsConsole();
            var stdout = Console.OpenStandardOutput();
            return await RunAsync(wire, console.Size, bytes => { stdout.Write(bytes); stdout.Flush(); },
                () => WindowsInputAsync(wire, console)).ConfigureAwait(false);
        }

        using var terminal = new UnixTerminal();
        return await RunAsync(wire, terminal.Size, UnixOut.Write, () => UnixInputAsync(wire, terminal))
            .ConfigureAwait(false);
    }

    private async Task<int> RunAsync(
        Wire wire, Func<(int Cols, int Rows)> size, Action<byte[]> write, Func<Task> input)
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
        write(Encoding.UTF8.GetBytes(EnterHost));

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
            write(Encoding.UTF8.GetBytes(RestoreHost));
        }

        if (_farewell is not null)
        {
            await Console.Out.WriteLineAsync($"fleet: {_farewell}").ConfigureAwait(false);
        }

        return 0;
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
        var records = new WindowsConsole.InputRecord[64];

        while (_running)
        {
            var n = console.Read(records, 50);
            if (n < 0)
            {
                log($"console input failed, error {System.Runtime.InteropServices.Marshal.GetLastPInvokeError()}");
                return;
            }

            for (var i = 0; i < n && _running; i++)
            {
                switch (records[i].EventType)
                {
                    case WindowsConsole.KeyEvent:
                        await OnKeyRecordAsync(wire, keys, records[i].Key).ConfigureAwait(false);
                        break;
                    case WindowsConsole.FocusEvent:
                        await Command(wire, records[i].SetFocus != 0 ? "focus-in" : "focus-out").ConfigureAwait(false);
                        break;
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

        foreach (var each in inputs)
        {
            await Send(
                wire,
                MessageType.Key,
                new KeyMessage
                {
                    Key = (int)each.Key,
                    Mods = (int)each.Mods,
                    Consumed = (int)each.Consumed,
                    Text = each.Utf8,
                    Action = (int)each.Action,
                    Unshifted = each.Unshifted,
                    Win32 = each == input ? win32 : null,
                },
                WireJsonContext.Default.KeyMessage).ConfigureAwait(false);
        }
    }

    private async Task UnixInputAsync(Wire wire, UnixTerminal terminal)
    {
        var buffer = new byte[4096];
        var pending = new List<byte>(4096);

        while (_running)
        {
            var n = await Task.Run(() => terminal.Read(buffer)).ConfigureAwait(false);
            if (n <= 0)
            {
                return;
            }

            pending.Clear();
            for (var i = 0; i < n; i++)
            {
                var command = prefix.OnByte(buffer[i]);
                if (command == PrefixCommand.None)
                {
                    pending.Add(buffer[i]);
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
                        await Chord(wire, Prefix.CommandFor(buffer[i])).ConfigureAwait(false);
                        break;
                }
            }

            await Flush(wire, pending).ConfigureAwait(false);
        }
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

        await Command(wire, command).ConfigureAwait(false);
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
