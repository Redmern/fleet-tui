using Fleet.Ports;

namespace Fleet.Platform.Clipboard;

public sealed class LinuxClipboardImageSource(ClipboardTool? run = null) : IClipboardImageSource
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
            if (await _run(program, args, ct).ConfigureAwait(false) is (0, { Length: > 0 } png))
            {
                return new ClipboardImage(png);
            }
        }

        return ClipboardImage.None;
    }
}
