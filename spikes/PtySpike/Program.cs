using System.Runtime.Versioning;
using System.Text;

namespace PtySpike;

public static class Program
{
    private static volatile bool _menuOpen;

    public static async Task<int> Main(string[] args)
    {
        var mode = args.Length > 0 ? args[0] : "--help";

        if (mode != "--scan" && mode != "--help" && !OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("this spike covers ConPTY, so it is Windows only");
            return 2;
        }

        return mode switch
        {
            "--scan" => Scan(),
            "--echo" => await EchoAsync().ConfigureAwait(false),
            "--attach" => await AttachAsync(args[1..]).ConfigureAwait(false),
            _ => Help(),
        };
    }

    private static int Help()
    {
        Console.WriteLine("""
            ptyspike - hand-written ConPTY, no third-party dependencies

              --scan            prefix detection over a synthetic byte stream
              --echo            spawn a child on a real ConPTY and read its output
              --attach <cmd>    raw passthrough, ctrl+s space overlay, then repaint

            examples:
              ptyspike --attach nvim
              ptyspike --attach claude
            """);
        return 0;
    }

    private static int Scan()
    {
        var scanner = new PrefixScanner();

        var script = new (string Name, byte[] Bytes)[]
        {
            ("plain text", "hi"u8.ToArray()),
            ("ctrl+s then space", [0x13, 0x20]),
            ("ctrl+s then x", [0x13, 0x78]),
            ("ctrl+s twice then space", [0x13, 0x13, 0x20]),
        };

        foreach (var (name, bytes) in script)
        {
            var outcomes = bytes.Select(scanner.Feed).Select(o => o.ToString());
            Console.WriteLine($"  {name,-24} -> {string.Join(", ", outcomes)}");
            scanner.Feed(0x1b);
        }

        return 0;
    }

    [SupportedOSPlatform("windows")]
    private static async Task<int> EchoAsync()
    {
        Console.WriteLine("spawning a child on a hand-written ConPTY...");

        ConPty.Trace = line => Console.WriteLine($"  trace           {line}");

        using var connection = ConPty.Spawn(
            "cmd.exe /c echo hello-from-conpty & ping -n 3 127.0.0.1",
            Environment.CurrentDirectory,
            80,
            24);

        Console.WriteLine($"  pid              {connection.Pid}");

        var text = new StringBuilder();

        var reader = Task.Run(() =>
        {
            var buffer = new byte[4096];

            while (true)
            {
                int read;

                try
                {
                    read = connection.ReaderStream.Read(buffer, 0, buffer.Length);
                }
                catch (Exception e) when (e is IOException or ObjectDisposedException)
                {
                    return;
                }

                if (read <= 0)
                {
                    return;
                }

                lock (text)
                {
                    text.Append(Encoding.UTF8.GetString(buffer, 0, read));
                }
            }
        });

        await Task.WhenAny(reader, Task.Delay(TimeSpan.FromSeconds(3))).ConfigureAwait(false);

        string captured;
        lock (text)
        {
            captured = text.ToString();
        }

        var saw = captured.Contains("hello-from-conpty", StringComparison.Ordinal);

        Console.WriteLine($"  saw child output {(saw ? "YES" : "NO")}");
        Console.WriteLine($"  bytes captured   {captured.Length}");
        Console.WriteLine($"  child exited     {connection.HasExited}");
        Console.WriteLine($"  resize accepted  {TryResize(connection)}");
        Console.WriteLine($"  preview          {Preview(captured)}");

        connection.Kill();

        return saw ? 0 : 1;
    }

    private static string Preview(string text)
    {
        if (text.Length == 0)
        {
            return "(nothing captured)";
        }

        var slice = text.Length > 180 ? text[..180] : text;
        var sb = new StringBuilder();

        foreach (var c in slice)
        {
            sb.Append(c switch
            {
                '' => "<ESC>",
                '\r' => "<CR>",
                '\n' => "<LF>",
                _ => c < ' ' ? $"<{(int)c:X2}>" : c.ToString(),
            });
        }

        return sb.ToString();
    }

    [SupportedOSPlatform("windows")]
    private static string TryResize(ConPtyConnection connection)
    {
        try
        {
            connection.Resize(100, 30);
            return "YES";
        }
        catch (Exception e)
        {
            return $"NO ({e.GetType().Name})";
        }
    }

    [SupportedOSPlatform("windows")]
    private static async Task<int> AttachAsync(string[] command)
    {
        if (command.Length == 0)
        {
            Console.Error.WriteLine("usage: ptyspike --attach <program> [args...]");
            return 2;
        }

        var size = TerminalSize();

        using var connection = ConPty.Spawn(
            string.Join(' ', command),
            Environment.CurrentDirectory,
            size.Cols,
            size.Rows);

        using var raw = new ConsoleRawMode();

        var stdout = Console.OpenStandardOutput();
        var stdin = Console.OpenStandardInput();

        var pump = Task.Run(async () =>
        {
            var buffer = new byte[8192];

            while (true)
            {
                int read;

                try
                {
                    read = await connection.ReaderStream.ReadAsync(buffer).ConfigureAwait(false);
                }
                catch (Exception e) when (e is IOException or ObjectDisposedException)
                {
                    break;
                }

                if (read <= 0)
                {
                    break;
                }

                if (_menuOpen)
                {
                    continue;
                }

                await stdout.WriteAsync(buffer.AsMemory(0, read)).ConfigureAwait(false);
                await stdout.FlushAsync().ConfigureAwait(false);
            }
        });

        var scanner = new PrefixScanner();
        var single = new byte[1];

        while (!connection.HasExited)
        {
            var read = await stdin.ReadAsync(single).ConfigureAwait(false);

            if (read <= 0)
            {
                break;
            }

            switch (scanner.Feed(single[0]))
            {
                case ScanOutcome.Forward:
                    await connection.WriterStream.WriteAsync(single.AsMemory(0, 1))
                        .ConfigureAwait(false);
                    await connection.WriterStream.FlushAsync().ConfigureAwait(false);
                    break;

                case ScanOutcome.Swallowed:
                    break;

                case ScanOutcome.OpenMenu:
                    await ShowOverlayAsync(stdout, stdin, connection).ConfigureAwait(false);
                    break;
            }
        }

        connection.Kill();
        await pump.ConfigureAwait(false);

        return 0;
    }

    [SupportedOSPlatform("windows")]
    private static async Task ShowOverlayAsync(
        Stream stdout, Stream stdin, ConPtyConnection connection)
    {
        _menuOpen = true;

        var size = TerminalSize();
        await WriteAsync(stdout, Panel(size.Cols, size.Rows)).ConfigureAwait(false);

        var single = new byte[1];
        await stdin.ReadAsync(single).ConfigureAwait(false);

        _menuOpen = false;

        connection.Resize(Math.Max(size.Cols - 1, 2), Math.Max(size.Rows - 1, 2));
        await Task.Delay(50).ConfigureAwait(false);
        connection.Resize(size.Cols, size.Rows);
    }

    private static string Panel(int cols, int rows)
    {
        var width = Math.Min(cols - 2, 62);

        var lines = new[]
        {
            " fleet menu (spike)",
            string.Empty,
            "   a   Add repository",
            "   r   Refresh",
            "   k   Keybinds",
            "   q   Close pane",
            string.Empty,
            "   any key dismisses, then the child repaints",
        };

        var top = Math.Max((rows - lines.Length - 2) / 2, 1);
        var left = Math.Max((cols - width) / 2, 1);

        var sb = new StringBuilder();
        sb.Append("[2J");
        sb.Append("[48;2;30;30;46m[38;2;205;214;244m");

        sb.Append($"[{top};{left}H");
        sb.Append('╭').Append(new string('─', width - 2)).Append('╮');

        for (var i = 0; i < lines.Length; i++)
        {
            var text = lines[i].Length > width - 2 ? lines[i][..(width - 2)] : lines[i];
            sb.Append($"[{top + i + 1};{left}H");
            sb.Append('│').Append(text.PadRight(width - 2)).Append('│');
        }

        sb.Append($"[{top + lines.Length + 1};{left}H");
        sb.Append('╰').Append(new string('─', width - 2)).Append('╯');
        sb.Append("[0m");

        return sb.ToString();
    }

    private static async Task WriteAsync(Stream stdout, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        await stdout.WriteAsync(bytes).ConfigureAwait(false);
        await stdout.FlushAsync().ConfigureAwait(false);
    }

    private static (int Cols, int Rows) TerminalSize()
    {
        try
        {
            return (Math.Max(Console.WindowWidth, 20), Math.Max(Console.WindowHeight, 5));
        }
        catch (IOException)
        {
            return (80, 24);
        }
    }
}
