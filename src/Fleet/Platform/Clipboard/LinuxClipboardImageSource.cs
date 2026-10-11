using Fleet.Ports;

namespace Fleet.Platform.Clipboard;

public sealed class LinuxClipboardImageSource(int maxBytes, ClipboardTool? run = null) : IClipboardImageSource
{
    private static readonly (string Program, string[] Args)[] Readers =
    [
        ("wl-paste", ["--no-newline", "--type", "image/png"]),
        ("xclip", ["-selection", "clipboard", "-t", "image/png", "-o"]),
    ];

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
}
