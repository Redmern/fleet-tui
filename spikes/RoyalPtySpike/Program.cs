using System.Text;
using RoyalTerminal.Terminal;

namespace RoyalPtySpike;

public static class Program
{
    private static readonly StringBuilder Captured = new();
    private static volatile bool _exited;

    public static async Task<int> Main(string[] args)
    {
        var mode = args.Length > 0 ? args[0] : "--echo";

        return mode switch
        {
            "--echo" => await EchoAsync().ConfigureAwait(false),
            "--keys" => await KeysAsync().ConfigureAwait(false),
            "--attach" => await AttachAsync(args[1..]).ConfigureAwait(false),
            _ => Help(),
        };
    }

    private static async Task<int> KeysAsync()
    {
        Console.WriteLine("press keys to see the bytes fleet would receive. ctrl+q quits.");
        Console.WriteLine();

        using var raw = new ConsoleRawMode();
        Console.WriteLine($"raw mode: {raw.Describe()}");
        Console.WriteLine();

        var stdin = Console.OpenStandardInput();
        var buffer = new byte[64];

        while (true)
        {
            var read = await stdin.ReadAsync(buffer).ConfigureAwait(false);

            if (read <= 0)
            {
                break;
            }

            var hex = string.Join(" ", buffer.Take(read).Select(b => b.ToString("X2")));
            var chars = string.Join(
                string.Empty,
                buffer.Take(read).Select(b => b >= 0x20 && b < 0x7f ? ((char)b).ToString() : "."));

            Console.Write($"  {read,2} byte(s)  {hex,-40}  {chars}\r\n");

            if (buffer[0] == 0x11)
            {
                break;
            }
        }

        return 0;
    }

    private static int Help()
    {
        Console.WriteLine("""
            royalptyspike - RoyalApps PTY under NativeAOT

              --echo            spawn a child, confirm its output arrives
              --keys            show the raw bytes each keypress delivers
              --attach <cmd>    raw passthrough with a ctrl+s space overlay
            """);
        return 0;
    }

    private static async Task<int> EchoAsync()
    {
        Console.WriteLine("creating a pty via DefaultPtyFactory...");

        using var pty = new DefaultPtyFactory().Create();

        Console.WriteLine($"  implementation   {pty.GetType().Name}");

        pty.DataReceived += (buffer, count) =>
        {
            lock (Captured)
            {
                Captured.Append(Encoding.UTF8.GetString(buffer, 0, count));
            }
        };

        pty.ProcessExited += _ => _exited = true;

        var shell = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh";
        var arguments = OperatingSystem.IsWindows()
            ? new[] { "/c", "echo hello-from-royal & ping -n 3 127.0.0.1" }
            : ["-c", "echo hello-from-royal; sleep 1"];

        pty.Start(
            shell,
            80,
            24,
            Environment.CurrentDirectory,
            new Dictionary<string, string> { ["TERM"] = "xterm-256color" },
            arguments);

        Console.WriteLine($"  child pid        {pty.ChildPid}");
        Console.WriteLine($"  is running       {pty.IsRunning}");

        var deadline = DateTime.UtcNow.AddSeconds(5);

        while (DateTime.UtcNow < deadline && !Seen("hello-from-royal"))
        {
            await Task.Delay(100).ConfigureAwait(false);
        }

        string captured;
        lock (Captured)
        {
            captured = Captured.ToString();
        }

        var saw = captured.Contains("hello-from-royal", StringComparison.Ordinal);

        Console.WriteLine($"  saw child output {(saw ? "YES" : "NO")}");
        Console.WriteLine($"  bytes captured   {captured.Length}");
        Console.WriteLine($"  child exited     {_exited}");
        Console.WriteLine($"  resize accepted  {TryResize(pty)}");
        Console.WriteLine($"  preview          {Preview(captured)}");

        pty.Stop();

        return saw ? 0 : 1;
    }

    private static bool Seen(string needle)
    {
        lock (Captured)
        {
            return Captured.ToString().Contains(needle, StringComparison.Ordinal);
        }
    }

    private static string TryResize(IPty pty)
    {
        try
        {
            pty.Resize(100, 30);
            return "YES";
        }
        catch (Exception e)
        {
            return $"NO ({e.GetType().Name})";
        }
    }

    private static string Preview(string text)
    {
        if (text.Length == 0)
        {
            return "(nothing captured)";
        }

        var slice = text.Length > 220 ? text[..220] : text;
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

    private static async Task<int> AttachAsync(string[] command)
    {
        if (command.Length == 0)
        {
            Console.Error.WriteLine("usage: royalptyspike --attach <program> [args...]");
            return 2;
        }

        var cols = Math.Max(Console.WindowWidth, 20);
        var rows = Math.Max(Console.WindowHeight, 5);

        using var pty = new DefaultPtyFactory().Create();
        using var raw = new ConsoleRawMode();

        var stdout = Console.OpenStandardOutput();
        var stdin = Console.OpenStandardInput();
        var menuOpen = false;

        pty.DataReceived += (buffer, count) =>
        {
            if (menuOpen)
            {
                return;
            }

            stdout.Write(buffer, 0, count);
            stdout.Flush();
        };

        pty.ProcessExited += _ => _exited = true;

        pty.Start(
            command[0],
            cols,
            rows,
            Environment.CurrentDirectory,
            new Dictionary<string, string> { ["TERM"] = "xterm-256color" },
            command[1..]);

        var prefixByte = PrefixByte();
        var scanner = new PrefixScanner(prefixByte);
        var single = new byte[1];

        async Task StatusAsync(string text)
        {
            var line = $"[s[{rows};1H[K[48;2;69;71;90m" +
                       $"[38;2;249;226;175m {text} [0m[u";

            await stdout.WriteAsync(Encoding.UTF8.GetBytes(line)).ConfigureAwait(false);
            await stdout.FlushAsync().ConfigureAwait(false);
        }

        await StatusAsync($"ptyspike: prefix=0x{prefixByte:X2} waiting").ConfigureAwait(false);

        while (!_exited)
        {
            var read = await stdin.ReadAsync(single).ConfigureAwait(false);

            if (read <= 0)
            {
                break;
            }

            switch (scanner.Feed(single[0]))
            {
                case ScanOutcome.Forward:
                    pty.Write(single, 0, 1);
                    break;

                case ScanOutcome.Swallowed:
                    await StatusAsync("ptyspike: PREFIX ARMED - press space").ConfigureAwait(false);
                    break;

                case ScanOutcome.OpenMenu:
                    menuOpen = true;
                    var panel = Encoding.UTF8.GetBytes(Overlay.Panel(cols, rows));
                    await stdout.WriteAsync(panel).ConfigureAwait(false);
                    await stdout.FlushAsync().ConfigureAwait(false);

                    await stdin.ReadAsync(single).ConfigureAwait(false);
                    menuOpen = false;

                    pty.Resize(Math.Max(cols - 1, 2), Math.Max(rows - 1, 2));
                    await Task.Delay(50).ConfigureAwait(false);
                    pty.Resize(cols, rows);
                    break;
            }
        }

        pty.Stop();
        return 0;
    }

    private static byte PrefixByte()
    {
        var configured = Environment.GetEnvironmentVariable("PTYSPIKE_PREFIX");

        if (!string.IsNullOrWhiteSpace(configured)
            && byte.TryParse(
                configured.Replace("0x", string.Empty, StringComparison.OrdinalIgnoreCase),
                System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture,
                out var parsed))
        {
            return parsed;
        }

        return 0x00;
    }
}
