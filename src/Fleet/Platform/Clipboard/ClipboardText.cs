using System.Text;

namespace Fleet.Platform.Clipboard;

public sealed class ClipboardText(
    IReadOnlyList<(string Program, string[] Args, string? Needs)> writers,
    IReadOnlyList<(string Program, string[] Args, string? Needs)> readers,
    int maxBytes,
    ClipboardFeed? feed = null,
    ClipboardTool? run = null,
    Func<string, string?>? env = null)
{
    private const string Wayland = "WAYLAND_DISPLAY";
    private const string X11 = "DISPLAY";

    private readonly ClipboardFeed _feed = feed ?? ClipboardTextFeed.FeedAsync;
    private readonly ClipboardTool _run = run ?? ClipboardTools.RunAsync;
    private readonly Func<string, string?> _env = env ?? Environment.GetEnvironmentVariable;
    private readonly Lock _gate = new();
    private (byte[] Bytes, TaskCompletionSource<ClipboardTextCopy> Done)? _waiting;
    private bool _copying;

    public static ClipboardText? ForThisMachine(int maxBytes) =>
        OperatingSystem.IsMacOS() ? Mac(maxBytes)
        : OperatingSystem.IsWindows() ? null
        : Linux(maxBytes);

    public static ClipboardText Linux(int maxBytes, ClipboardFeed? feed = null, ClipboardTool? run = null, Func<string, string?>? env = null) =>
        new(
            [("wl-copy", [], Wayland), ("xclip", ["-selection", "clipboard", "-i"], X11)],
            [("wl-paste", ["--no-newline"], Wayland), ("xclip", ["-selection", "clipboard", "-o"], X11)],
            maxBytes,
            feed,
            run,
            env);

    public static ClipboardText Mac(int maxBytes, ClipboardFeed? feed = null, ClipboardTool? run = null, Func<string, string?>? env = null) =>
        new([("pbcopy", [], null)], [("pbpaste", [], null)], maxBytes, feed, run, env);

    public Task<ClipboardTextCopy> CopyAsync(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        if (bytes.Length > maxBytes)
        {
            return Task.FromResult(ClipboardTextCopy.TooLarge);
        }

        var done = new TaskCompletionSource<ClipboardTextCopy>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            if (_copying)
            {
                _waiting?.Done.SetResult(ClipboardTextCopy.Superseded);
                _waiting = (bytes, done);
                return done.Task;
            }

            _copying = true;
        }

        _ = Task.Run(() => CopyFromAsync(bytes, done));
        return done.Task;
    }

    public async Task<string?> ReadAsync(CancellationToken ct = default)
    {
        foreach (var (program, args, _) in Usable(readers))
        {
            if (await _run(program, args, maxBytes + 1, ct).ConfigureAwait(false) is not { Code: 0, Stdout: { } stdout } read)
            {
                continue;
            }

            return read.Truncated || stdout.Length > maxBytes || stdout.Length == 0 ? null : Encoding.UTF8.GetString(stdout);
        }

        return null;
    }

    private async Task CopyFromAsync(byte[] bytes, TaskCompletionSource<ClipboardTextCopy> done)
    {
        while (true)
        {
            done.SetResult(await FeedFirstAsync(bytes).ConfigureAwait(false));
            lock (_gate)
            {
                if (_waiting is not { } waiting)
                {
                    _copying = false;
                    return;
                }

                (bytes, done) = waiting;
                _waiting = null;
            }
        }
    }

    private async Task<ClipboardTextCopy> FeedFirstAsync(byte[] bytes)
    {
        foreach (var (program, args, _) in Usable(writers))
        {
            if (await TryFeedAsync(program, args, bytes).ConfigureAwait(false) == 0)
            {
                return new ClipboardTextCopy(ClipboardTextResult.Set, program);
            }
        }

        return ClipboardTextCopy.NotSet;
    }

    private async Task<int?> TryFeedAsync(string program, string[] args, byte[] bytes)
    {
        try
        {
            return await _feed(program, args, bytes, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private IEnumerable<(string Program, string[] Args, string? Needs)> Usable(
        IReadOnlyList<(string Program, string[] Args, string? Needs)> tools) =>
        tools.Where(tool => tool.Needs is null || !string.IsNullOrEmpty(_env(tool.Needs)));
}
