using System.Text;
using Porta.Pty;

namespace PtySpike;

public static class Program
{
    private static volatile bool _menuOpen;
    private static volatile bool _childExited;

    public static async Task<int> Main(string[] args)
    {
        var mode = args.Length > 0 ? args[0] : "--help";

        return mode switch
        {
            "--echo" => await EchoAsync().ConfigureAwait(false),
            "--scan" => Scan(),
            "--attach" => await AttachAsync(args[1..]).ConfigureAwait(false),
            _ => Help(),
        };
    }

    private static int Help()
    {
        Console.WriteLine("""
            ptyspike - answers three questions about fleet owning a child terminal

              --scan            prefix detection over a synthetic byte stream (no PTY)
              --echo            spawn a child on a PTY and read its output (no raw mode)
              --attach <cmd>    raw passthrough with ctrl+s space overlay, then repaint

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

    private static async Task<int> EchoAsync()
    {
        var shell = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh";
        var line = OperatingSystem.IsWindows()
            ? new[] { "cmd.exe", "/c", "echo hello-from-pty" }
            : ["/bin/sh", "-c", "echo hello-from-pty"];

        var options = new PtyOptions
        {
            Name = "ptyspike",
            Cols = 80,
            Rows = 24,
            Cwd = Environment.CurrentDirectory,
            App = shell,
            CommandLine = line,
            Environment = new Dictionary<string, string> { ["TERM"] = "xterm-256color" },
        };

        Console.WriteLine("spawning a child on a PTY...");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var connection = await PtyProvider.SpawnAsync(options, cts.Token).ConfigureAwait(false);

        Console.WriteLine($"  pid            {connection.Pid}");

        var buffer = new byte[4096];
        var text = new StringBuilder();
        var deadline = DateTime.UtcNow.AddSeconds(5);

        while (DateTime.UtcNow < deadline)
        {
            var read = await connection.ReaderStream.ReadAsync(buffer, cts.Token)
                .ConfigureAwait(false);

            if (read <= 0)
            {
                break;
            }

            text.Append(Encoding.UTF8.GetString(buffer, 0, read));

            if (text.ToString().Contains("hello-from-pty", StringComparison.Ordinal))
            {
                break;
            }
        }

        connection.Kill();

        var saw = text.ToString().Contains("hello-from-pty", StringComparison.Ordinal);
        Console.WriteLine($"  saw child output  {(saw ? "YES" : "NO")}");
        Console.WriteLine($"  raw bytes         {text.Length}");

        return saw ? 0 : 1;
    }

    private static async Task<int> AttachAsync(string[] command)
    {
        if (command.Length == 0)
        {
            Console.Error.WriteLine("usage: ptyspike --attach <program> [args...]");
            return 2;
        }

        var size = TerminalSize();

        var options = new PtyOptions
        {
            Name = "ptyspike",
            Cols = size.Cols,
            Rows = size.Rows,
            Cwd = Environment.CurrentDirectory,
            App = command[0],
            CommandLine = command,
            Environment = new Dictionary<string, string> { ["TERM"] = "xterm-256color" },
        };

        var connection = await PtyProvider.SpawnAsync(options, CancellationToken.None)
            .ConfigureAwait(false);

        connection.ProcessExited += (_, _) => _childExited = true;

        using var raw = new ConsoleRawMode();

        var stdout = Console.OpenStandardOutput();
        var stdin = Console.OpenStandardInput();

        var pump = Task.Run(async () =>
        {
            var buffer = new byte[8192];

            while (!_childExited)
            {
                int read;

                try
                {
                    read = await connection.ReaderStream.ReadAsync(buffer).ConfigureAwait(false);
                }
                catch (IOException)
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

        while (!_childExited)
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

        await pump.ConfigureAwait(false);
        connection.Kill();

        return 0;
    }

    private static async Task ShowOverlayAsync(
        Stream stdout, Stream stdin, IPtyConnection connection)
    {
        _menuOpen = true;

        var size = TerminalSize();
        await WriteAsync(stdout, Panel(size.Cols, size.Rows)).ConfigureAwait(false);

        var single = new byte[1];
        await stdin.ReadAsync(single).ConfigureAwait(false);

        _menuOpen = false;

        connection.Resize(Math.Max(size.Cols - 1, 2), Math.Max(size.Rows - 1, 2));
        await Task.Delay(40).ConfigureAwait(false);
        connection.Resize(size.Cols, size.Rows);
    }

    private static string Panel(int cols, int rows)
    {
        var width = Math.Min(cols - 2, 60);
        var lines = new[]
        {
            "fleet menu (spike)",
            string.Empty,
            "  a   Add repository",
            "  r   Refresh",
            "  k   Keybinds",
            "  q   Close pane",
            string.Empty,
            "  press any key to dismiss and repaint the child",
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
            sb.Append($"[{top + i + 1};{left}H");
            sb.Append('│');
            sb.Append(lines[i].PadRight(width - 2)[..(width - 2)]);
            sb.Append('│');
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
