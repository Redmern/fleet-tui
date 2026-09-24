using RoyalTerminal.Terminal;

namespace EmbeddedSpike.Pty;

// ConPTY through RoyalApps.RoyalTerminal.Terminal.Pty.Windows, the library the
// 2026-08-08 spike found AOT-clean. Only the Windows package is referenced: the
// Platform package also pulls in the Unix one, which calls forkpty from managed
// code (see UnixPanePty for why that is avoided).
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal sealed class WindowsPanePty : IPanePty
{
    private readonly IPty _pty = new WindowsPty();

    public event Action<byte[], int>? Output;

    public event Action<int>? Exited;

    public void Start(string program, IReadOnlyList<string> args, int cols, int rows)
    {
        _pty.DataReceived += (buffer, count) => Output?.Invoke(buffer, count);
        _pty.ProcessExited += code => Exited?.Invoke(code);

        var environment = new Dictionary<string, string>
        {
            ["TERM"] = "xterm-256color",
            ["COLORTERM"] = "truecolor",
        };

        _pty.Start(ResolveProgram(program), cols, rows, Environment.CurrentDirectory, environment, [.. args]);
    }

    public void Write(ReadOnlySpan<byte> data)
    {
        var copy = data.ToArray();
        _pty.Write(copy, 0, copy.Length);
    }

    public void Resize(int cols, int rows) => _pty.Resize(cols, rows);

    public void Dispose()
    {
        try
        {
            _pty.Stop();
        }
        catch (InvalidOperationException)
        {
        }

        _pty.Dispose();
    }

    // CreateProcess does not consult PATHEXT, so "claude" must become the
    // claude.exe (or .cmd) that a shell would have found.
    private static string ResolveProgram(string program)
    {
        if (Path.IsPathRooted(program) || Path.HasExtension(program))
        {
            return program;
        }

        var extensions = (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT")
            .Split(';', StringSplitOptions.RemoveEmptyEntries);
        var dirs = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        foreach (var dir in dirs)
        {
            foreach (var ext in extensions)
            {
                var candidate = Path.Combine(dir, program + ext.ToLowerInvariant());
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return program;
    }
}
