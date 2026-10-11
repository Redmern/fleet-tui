using Fleet.Ports;

namespace Fleet.Platform.Clipboard;

public sealed class LinuxClipboardImageSource(int maxBytes, ClipboardTool? run = null) : IClipboardImageSource
{
    private static readonly (string Program, string[] Args)[] Readers =
    [
        ("wl-paste", ["--no-newline", "--type", "image/png"]),
        ("xclip", ["-selection", "clipboard", "-t", "image/png", "-o"]),
    ];

    private static readonly (string Program, string[] Args)[] Listers =
    [
        ("wl-paste", ["--list-types"]),
        ("xclip", ["-selection", "clipboard", "-t", "TARGETS", "-o"]),
    ];

    private const int MaxTypeList = 64 * 1024;

    private readonly ClipboardTool _run = run ?? ClipboardTools.RunAsync;

    public async Task<ClipboardImage> ReadPngAsync(CancellationToken ct = default)
    {
        foreach (var (program, args) in Readers)
        {
            if (await _run(program, args, maxBytes + 1, ct).ConfigureAwait(false) is { Code: 0, Stdout: { Length: > 0 } png })
            {
                return new ClipboardImage(png);
            }
        }

        return ClipboardImage.None;
    }

    public async Task<ClipboardImage> ProbePngAsync(CancellationToken ct = default)
    {
        foreach (var (program, args) in Listers)
        {
            if (await _run(program, args, MaxTypeList, ct).ConfigureAwait(false) is { Code: 0, Truncated: false, Stdout: { } listed })
            {
                return System.Text.Encoding.UTF8.GetString(listed)
                    .Split('\n', StringSplitOptions.TrimEntries)
                    .Contains("image/png", StringComparer.Ordinal)
                    ? ClipboardImage.Available
                    : ClipboardImage.None;
            }
        }

        return ClipboardImage.None;
    }
}
