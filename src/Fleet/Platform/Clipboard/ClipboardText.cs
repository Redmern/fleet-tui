using System.Text;

namespace Fleet.Platform.Clipboard;

public sealed class ClipboardText(
    IReadOnlyList<(string Program, string[] Args)> writers,
    IReadOnlyList<(string Program, string[] Args)> readers,
    int maxBytes,
    ClipboardFeed? feed = null,
    ClipboardTool? run = null)
{
    private readonly ClipboardFeed _feed = feed ?? ClipboardTextFeed.FeedAsync;
    private readonly ClipboardTool _run = run ?? ClipboardTools.RunAsync;

    public static ClipboardText? ForThisMachine(int maxBytes) =>
        OperatingSystem.IsMacOS() ? Mac(maxBytes)
        : OperatingSystem.IsWindows() ? null
        : Linux(maxBytes);

    public static ClipboardText Linux(int maxBytes, ClipboardFeed? feed = null, ClipboardTool? run = null) =>
        new(
            [("wl-copy", []), ("xclip", ["-selection", "clipboard", "-i"])],
            [("wl-paste", ["--no-newline"]), ("xclip", ["-selection", "clipboard", "-o"])],
            maxBytes,
            feed,
            run);

    public static ClipboardText Mac(int maxBytes, ClipboardFeed? feed = null, ClipboardTool? run = null) =>
        new([("pbcopy", [])], [("pbpaste", [])], maxBytes, feed, run);

    public async Task<string?> CopyAsync(string text, CancellationToken ct = default)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        foreach (var (program, args) in writers)
        {
            if (await _feed(program, args, bytes, ct).ConfigureAwait(false) == 0)
            {
                return program;
            }
        }

        return null;
    }

    public async Task<string?> ReadAsync(CancellationToken ct = default)
    {
        foreach (var (program, args) in readers)
        {
            if (await _run(program, args, maxBytes + 1, ct).ConfigureAwait(false) is not { Code: 0, Stdout: { } stdout } read)
            {
                continue;
            }

            return read.Truncated || stdout.Length > maxBytes || stdout.Length == 0 ? null : Encoding.UTF8.GetString(stdout);
        }

        return null;
    }
}
