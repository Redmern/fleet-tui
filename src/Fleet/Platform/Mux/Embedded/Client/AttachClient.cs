using System.Diagnostics;
using System.Text;
using System.Threading.Channels;
using Fleet.Platform.Clipboard;
using Fleet.Platform.Mux.Embedded.Host;
using Fleet.Platform.Mux.Embedded.Input;
using Fleet.Platform.Mux.Embedded.Native;
using Fleet.Platform.Mux.Embedded.Protocol;
using Fleet.Ports;
using Fleet.Shared.Constants;

namespace Fleet.Platform.Mux.Embedded.Client;

public sealed class AttachClient(
    Stream stream,
    string? workspace,
    Func<MuxKeys> loadKeys,
    Action<string> log,
    bool mouse = true,
    Func<string, bool>? openWindow = null,
    Func<string, string, bool>? openRemote = null,
    Action<Hello>? furnish = null,
    Action<bool, int>? forwardPort = null,
    IClipboardImageSource? clipboardImages = null,
    Action<string>? clipboardNotice = null,
    ClipboardText? clipboardText = null)
{
    private const string EnterHost = "\e[?1049h\e[H\e[2J";

    private const string RestoreHost =
        "\e[?2026l\e[0m\e[?1000l\e[?1002l\e[?1003l\e[?1006l\e[?1004l\e[?2004l" +
        "\e[?1l\e>\e[0 q\e[?25h\e[?1049l";

    private const string PushTitle = "\e[22;0t";
    private const string PopTitle = "\e[23;0t";

    private readonly HashSet<ushort> _swallowed = [];
    private readonly FloatMode _floatMode = new();
    private readonly CopyMode _copyMode = new();
    private readonly ConfirmMode _confirm = new();
    private MuxKeys _keys = loadKeys();
    private Prefix? _prefixState;

    private Prefix PrefixState => _prefixState ??= new Prefix(_keys.Prefix);
    private volatile bool _running = true;
    private int _left;
    private string? _farewell;

    public async Task<int> RunAsync()
    {
        using var wire = new Wire(stream);
        var (cols, rows) = CookedSize();

        if (await HandshakeAsync(wire, cols, rows).ConfigureAwait(false) is not { } client)
        {
            return 1;
        }

        if (OperatingSystem.IsWindows())
        {
            using var console = new WindowsConsole(mouse);
            var stdout = Console.OpenStandardOutput();
            return await RunAsync(wire, client, (cols, rows), console.Size, bytes => { stdout.Write(bytes); stdout.Flush(); },
                () => WindowsInputAsync(wire, console), console.Dispose).ConfigureAwait(false);
        }

        using var terminal = new UnixTerminal();
        return await RunAsync(wire, client, (cols, rows), terminal.Size, UnixOut.Write, () => UnixInputAsync(wire, terminal), terminal.Dispose)
            .ConfigureAwait(false);
    }

    private static (int Cols, int Rows) CookedSize()
    {
        try
        {
            return Console.WindowWidth > 0 && Console.WindowHeight > 0 ? (Console.WindowWidth, Console.WindowHeight) : (80, 24);
        }
        catch (Exception e) when (e is IOException or PlatformNotSupportedException)
        {
            return (80, 24);
        }
    }

    private async Task<string?> HandshakeAsync(Wire wire, int cols, int rows)
    {
        var hello = new Hello
        {
            Version = Wire.OldestVersion,
            Highest = Wire.Version,
            Build = FleetVersion.Current,
            Role = ClientRoles.Attach,
            Os = OperatingSystem.IsWindows() ? "windows" : "unix",
            Cols = cols,
            Rows = rows,
            Workspace = workspace,
        };
        furnish?.Invoke(hello);

        await wire.SendAsync(MessageType.Hello, hello, WireJsonContext.Default.Hello).ConfigureAwait(false);

        (MessageType Type, byte[] Payload)? welcome;
        try
        {
            welcome = await wire.ReceiveAsync().ConfigureAwait(false);
        }
        catch (StrayBytesException e)
        {
            var text = e.Header.Concat(await StrayTextAsync().ConfigureAwait(false)).ToArray();
            await Console.Error.WriteLineAsync(
                "fleet: the other end sent text instead of fleet's protocol. On ssh this is usually the remote's shell " +
                "startup printing something, or an old or different 'fleet' on its PATH (set FLEET_REMOTE_COMMAND). It sent:" +
                Environment.NewLine + Encoding.UTF8.GetString(text).TrimEnd()).ConfigureAwait(false);
            return null;
        }

        if (welcome is not { } reply)
        {
            await Console.Error.WriteLineAsync("fleet: fleetd closed the connection").ConfigureAwait(false);
            return null;
        }

        if (reply.Type == MessageType.Error)
        {
            await Console.Error.WriteLineAsync(
                $"fleet: {Wire.Read(reply.Payload, WireJsonContext.Default.ErrorMessage).Message}").ConfigureAwait(false);
            return null;
        }

        var welcomed = Wire.Read(reply.Payload, WireJsonContext.Default.Welcome);
        if (Wire.Refusal(welcomed) is { } refused)
        {
            await Console.Error.WriteLineAsync($"fleet: {refused}").ConfigureAwait(false);
            return null;
        }

        return welcomed.Client;
    }

    private async Task<byte[]> StrayTextAsync()
    {
        var buffer = new byte[400];
        var read = 0;
        using var quiet = new CancellationTokenSource(TimeSpan.FromSeconds(1));

        try
        {
            while (read < buffer.Length)
            {
                var n = await stream.ReadAsync(buffer.AsMemory(read), quiet.Token).ConfigureAwait(false);
                if (n == 0)
                {
                    break;
                }

                read += n;
            }
        }
        catch (Exception e) when (e is OperationCanceledException or IOException)
        {
        }

        return buffer[..read];
    }
    private async Task<int> RunAsync(
        Wire wire, string client, (int Cols, int Rows) told, Func<(int Cols, int Rows)> size, Action<byte[]> write, Func<Task> input, Action release)
    {
        var (cols, rows) = told;
        log($"attached as {client} at {cols}x{rows}");
        var savedTitle = HostEffectsOut.CurrentConsoleTitle();
        write(Encoding.UTF8.GetBytes(EnterSequence(OperatingSystem.IsWindows(), mouse)));

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

    public static string EnterSequence(bool windows, bool mouse) =>
        windows
            ? EnterHost
            : PushTitle + EnterHost + (mouse ? SgrMouse.Enable : string.Empty) + ModifiedKeys.Enable + PasteMarkers.Enable;

    public static string RestoreSequence => RestoreHost + (OperatingSystem.IsWindows() ? string.Empty : ModifiedKeys.Disable + PopTitle);

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

    public static bool Forward(HostEffect effect, Action<bool, int>? forwardPort)
    {
        if (forwardPort is null
            || effect.Kind is not (HostEffects.ForwardPort or HostEffects.UnforwardPort)
            || !int.TryParse(effect.Value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var port))
        {
            return false;
        }

        forwardPort(effect.Kind == HostEffects.ForwardPort, port);
        return true;
    }

    private void Apply(HostEffect effect, Action<byte[]> write)
    {
        if (Forward(effect, forwardPort))
        {
            log($"{effect.Kind} {effect.Value}");
            return;
        }

        switch (effect.Kind)
        {
            case HostEffects.Title when effect.Value is { } title:
                write(Encoding.UTF8.GetBytes(HostEffectsOut.TitleSequence(title)));
                break;

            case HostEffects.OpenRemote when effect.Value?.Split('\n', 2) is [var host, var project]:
                log($"open {project} on {host} in a new window: {(openRemote?.Invoke(project, host) is true ? "opened" : "NOT opened")}");
                break;

            case HostEffects.Bell:
                write("\a"u8.ToArray());
                break;

            case HostEffects.OpenWindow when effect.Value is { } project:
                log($"open {project} in a new window: {(openWindow?.Invoke(project) is true ? "opened" : "NOT opened")}");
                break;

            case HostEffects.Clipboard when effect.Value is { } text:
                if (OperatingSystem.IsWindows())
                {
                    log($"clipboard: {text.Length} chars {(HostEffectsOut.SetWindowsClipboard(text) ? "set" : "NOT set")}");
                }
                else
                {
                    _ = LogCopyAsync(text, HostEffectsOut.CopyTextAsync(text, write, clipboardText));
                }

                break;
        }
    }

    private async Task LogCopyAsync(string text, Task<ClipboardTextCopy> copying)
    {
        try
        {
            var copied = await copying.ConfigureAwait(false);
            log(copied.Result switch
            {
                ClipboardTextResult.Set => $"clipboard: {text.Length} chars set with osc 52 and {copied.By}",
                ClipboardTextResult.TooLarge => $"clipboard: {text.Length} chars NOT set: too large to copy",
                ClipboardTextResult.Superseded => $"clipboard: {text.Length} chars superseded by a newer copy",
                _ => $"clipboard: {text.Length} chars NOT set: osc 52 sent, but no wl-copy, xclip or pbcopy took it",
            });
        }
        catch (Exception e)
        {
            log($"clipboard: {text.Length} chars NOT set: {e.GetType().Name}: {e.Message}");
        }
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
                    case MessageType.ClipboardImageRequest:
                        var asked = Wire.Read(message.Payload, WireJsonContext.Default.ClipboardImageRequest);
                        _ = Task.Run(() => AnswerClipboardImageAsync(wire, asked), CancellationToken.None);
                        break;
                    case MessageType.Bye:
                        _farewell = message.Payload.Length > 0
                            ? Encoding.UTF8.GetString(message.Payload)
                            : "fleetd ended the session";
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

    private async Task AnswerClipboardImageAsync(Wire wire, ClipboardImageRequest asked)
    {
        var answer = clipboardImages is null
            ? ClipboardImageResponse.For(asked.Id, null)
            : await ClipboardImageAnswer.ForAsync(asked, clipboardImages, clipboardNotice).ConfigureAwait(false);
        log($"clipboard image for {asked.Pane}: {(answer.Data is { } png ? $"{png.Length} bytes" : answer.Error ?? "no image")}");
        await Send(wire, MessageType.ClipboardImageResponse, answer, WireJsonContext.Default.ClipboardImageResponse).ConfigureAwait(false);
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

            if (!PrefixState.Armed && Sticky is null && PasteBurst.Starts(records.AsSpan(0, n)))
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

        if (Sticky is { } mode)
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
                    await ModeStep(wire, mode, mode.OnKey(step.Key, step.Mods, step.Utf8)).ConfigureAwait(false);
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

        var key = new KeyMessage
        {
            Key = (int)input.Key,
            Mods = (int)input.Mods,
            Consumed = (int)input.Consumed,
            Text = input.Utf8,
            Action = (int)input.Action,
            Unshifted = input.Unshifted,
            Repeat = inputs.Count,
            Win32 = win32,
        };

        var command = input.Action == KeyAction.Release
            ? PrefixCommand.None
            : PrefixState.OnKey(_keys.Root, input.Key, input.Mods, input.Utf8);

        switch (command)
        {
            case PrefixCommand.Armed or PrefixCommand.Descend or PrefixCommand.Back:
                _swallowed.Add(input.VirtualKey);
                await ShowWhichKey(wire).ConfigureAwait(false);
                return;
            case PrefixCommand.Chord:
                _swallowed.Add(input.VirtualKey);
                await Badge(wire, null).ConfigureAwait(false);
                await Chord(wire, PrefixState.Command, key, null).ConfigureAwait(false);
                return;
            case PrefixCommand.Cancel:
                _swallowed.Add(input.VirtualKey);
                await Badge(wire, null).ConfigureAwait(false);
                return;
            case PrefixCommand.SendPrefix:
                await Badge(wire, null).ConfigureAwait(false);
                break;
            case PrefixCommand.None when input.Action != KeyAction.Release
                                         && _keys.DirectCommand(input.Key, input.Mods, input.Utf8) is { } direct:
                _swallowed.Add(input.VirtualKey);
                await Chord(wire, direct, key, null).ConfigureAwait(false);
                return;
        }

        await Send(wire, MessageType.Key, key, WireJsonContext.Default.KeyMessage).ConfigureAwait(false);
    }

    private async Task UnixInputAsync(Wire wire, UnixTerminal terminal)
    {
        var reads = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });

        _ = Task.Run(() =>
        {
            var buffer = new byte[4096];
            int n;
            while (_running && (n = terminal.Read(buffer)) > 0)
            {
                reads.Writer.TryWrite(buffer[..n]);
            }

            reads.Writer.TryComplete();
        });

        await StdinAsync(wire, reads.Reader).ConfigureAwait(false);
    }

    public async Task StdinAsync(Wire wire, ChannelReader<byte[]> reads)
    {
        var pending = new List<byte>(4096);
        var sgr = new SgrMouse();
        var markers = new PasteMarkers();
        var clock = Stopwatch.StartNew();

        while (_running)
        {
            var wait = markers.Deadline is { } due
                ? TimeSpan.FromTicks(Math.Max((due - clock.Elapsed).Ticks, 0))
                : Timeout.InfiniteTimeSpan;
            var (read, closed) = await NextReadAsync(reads, wait).ConfigureAwait(false);

            var chunks = read is not null ? markers.Feed(read, clock.Elapsed)
                : markers.Expire(closed ? TimeSpan.MaxValue : clock.Elapsed);
            foreach (var chunk in chunks)
            {
                await StdinChunkAsync(wire, chunk, sgr, pending).ConfigureAwait(false);
            }

            if (closed)
            {
                return;
            }
        }
    }

    private static async Task<(byte[]? Read, bool Closed)> NextReadAsync(ChannelReader<byte[]> reads, TimeSpan wait)
    {
        if (reads.TryRead(out var ready))
        {
            return (ready, false);
        }

        using var quiet = new CancellationTokenSource(wait);
        try
        {
            if (!await reads.WaitToReadAsync(quiet.Token).ConfigureAwait(false))
            {
                return (null, true);
            }

            return reads.TryRead(out var read) ? (read, false) : (null, false);
        }
        catch (OperationCanceledException)
        {
            return (null, false);
        }
    }

    private async Task StdinChunkAsync(Wire wire, PasteChunk chunk, SgrMouse sgr, List<byte> pending)
    {
        if (chunk.Pasted)
        {
            await PastedAsync(wire, chunk.Bytes).ConfigureAwait(false);
            return;
        }

        foreach (var item in mouse ? sgr.Feed(chunk.Bytes) : [chunk.Bytes])
        {
            if (item is MouseMessage message)
            {
                await Send(wire, MessageType.Mouse, message, WireJsonContext.Default.MouseMessage).ConfigureAwait(false);
                continue;
            }

            await TypedBytesAsync(wire, (byte[])item, pending).ConfigureAwait(false);
        }
    }

    private async Task PastedAsync(Wire wire, byte[] bytes)
    {
        if (Sticky is not null)
        {
            log($"paste: {bytes.Length} bytes dropped in a mode that reads keys as commands");
            return;
        }

        if (PrefixState.Armed)
        {
            PrefixState.Disarm();
            await Badge(wire, null).ConfigureAwait(false);
        }

        log($"paste: {bytes.Length} bytes");
        await Send(wire, MessageType.Text, new TextMessage { Text = Encoding.UTF8.GetString(bytes), Paste = true },
            WireJsonContext.Default.TextMessage).ConfigureAwait(false);
    }

    private async Task TypedBytesAsync(Wire wire, byte[] bytes, List<byte> pending)
    {
        pending.Clear();
        bytes = ModifiedKeys.Normalize(bytes);
        var prefixBytes = PrefixState.Bytes;
        var i = 0;

        while (i < bytes.Length)
        {
            if (Sticky is { } mode)
            {
                await Flush(wire, pending).ConfigureAwait(false);
                i += mode.OnBytes(bytes.AsSpan(i), out var step);
                await ModeStep(wire, mode, step).ConfigureAwait(false);
                continue;
            }

            var startsWithPrefix = prefixBytes is not null && bytes.AsSpan(i).StartsWith(prefixBytes);

            if (PrefixState.Armed)
            {
                await Flush(wire, pending).ConfigureAwait(false);
                var command = PrefixState.OnBytes(_keys.Root, bytes.AsSpan(i), out var length);
                var original = bytes.AsSpan(i, length).ToArray();
                i += length;

                switch (command)
                {
                    case PrefixCommand.Armed or PrefixCommand.Descend or PrefixCommand.Back:
                        await ShowWhichKey(wire).ConfigureAwait(false);
                        break;
                    case PrefixCommand.SendPrefix:
                        await Badge(wire, null).ConfigureAwait(false);
                        pending.AddRange(original);
                        break;
                    case PrefixCommand.Chord:
                        await Badge(wire, null).ConfigureAwait(false);
                        await Chord(wire, PrefixState.Command, null, original).ConfigureAwait(false);
                        break;
                    default:
                        await Badge(wire, null).ConfigureAwait(false);
                        break;
                }

                continue;
            }

            if (startsWithPrefix)
            {
                await Flush(wire, pending).ConfigureAwait(false);
                PrefixState.Arm(_keys.Root);
                i += prefixBytes!.Length;
                await ShowWhichKey(wire).ConfigureAwait(false);
                continue;
            }

            if (i == 0 && _keys.DirectBytes(bytes) is { Command: { } direct, Length: var whole } && whole == bytes.Length)
            {
                i += whole;
                await Chord(wire, direct, null, bytes).ConfigureAwait(false);
                continue;
            }

            if (ModifiedKeys.Parse(bytes.AsSpan(i)) is var (modifier, code, reported))
            {
                pending.AddRange(ModifiedKeys.Legacy(modifier, code));
                i += reported;
                continue;
            }

            pending.Add(bytes[i]);
            i++;
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

    private async Task Chord(Wire wire, string? binding, KeyMessage? key, byte[]? bytes)
    {
        if (binding is null)
        {
            return;
        }

        var space = binding.IndexOf(' ');
        var (name, arg) = space < 0 ? (binding, null) : (binding[..space], binding[(space + 1)..].Trim());

        switch (name)
        {
            case "detach":
                _running = false;
                return;
            case "float-mode":
                _floatMode.Enter();
                await Badge(wire, FloatMode.Badge).ConfigureAwait(false);
                return;
            case "copy-mode":
                _copyMode.Enter();
                await Command(wire, name).ConfigureAwait(false);
                await Badge(wire, CopyMode.Badge).ConfigureAwait(false);
                return;
            case "kill-pane" or "kill-tab":
                _confirm.Ask(name, name == "kill-pane" ? "close this pane?" : "close this tab?");
                await Badge(wire, _confirm.Badge).ConfigureAwait(false);
                return;
            case "paste":
                await PasteClipboard(wire).ConfigureAwait(false);
                return;
            case "reload":
                _keys = loadKeys();
                _prefixState = null;
                log($"keys reloaded: prefix {_keys.Prefix.Label}, {_keys.PrefixKeys.Count} prefix keys, {_keys.DirectKeys.Count} direct keys");
                await Command(wire, "redraw").ConfigureAwait(false);
                return;
        }

        await Send(
            wire,
            MessageType.Command,
            new CommandMessage
            {
                Name = name,
                Arg = arg,
                Key = key,
                Bytes = bytes is null ? null : Convert.ToBase64String(bytes),
            },
            WireJsonContext.Default.CommandMessage).ConfigureAwait(false);
    }

    private async Task PasteClipboard(Wire wire)
    {
        if (await HostEffectsOut.ReadTextAsync(clipboardText).ConfigureAwait(false) is { Length: > 0 } text)
        {
            await Send(wire, MessageType.Text, new TextMessage { Text = text, Paste = true },
                WireJsonContext.Default.TextMessage).ConfigureAwait(false);
        }
    }

    private Task ShowWhichKey(Wire wire) =>
        Send(
            wire,
            MessageType.Badge,
            new BadgeMessage
            {
                Text = PrefixState.Breadcrumb,
                Keys = WhichKey.For(PrefixState.Node ?? _keys.Root, _keys.Prefix, _keys.ShowIcons),
            },
            WireJsonContext.Default.BadgeMessage);

    private IStickyMode? Sticky =>
        _floatMode.Active ? _floatMode : _copyMode.Active ? _copyMode : _confirm.Active ? _confirm : null;
    private async Task ModeStep(Wire wire, IStickyMode mode, CommandMessage? step)
    {
        if (step is not null)
        {
            await Send(wire, MessageType.Command, step, WireJsonContext.Default.CommandMessage).ConfigureAwait(false);
        }

        if (!mode.Active)
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
