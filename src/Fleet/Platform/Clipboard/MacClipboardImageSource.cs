using System.Text;
using Fleet.Ports;

namespace Fleet.Platform.Clipboard;

public sealed class MacClipboardImageSource(int maxBytes, ClipboardTool? run = null) : IClipboardImageSource
{
    private const string PngClass = "«class PNGf»";
    private const string DataPrefix = "«data PNGf";
    private const int MaxInfo = 64 * 1024;
    private const int AppleScriptFraming = 32;

    private readonly ClipboardTool _run = run ?? ClipboardTools.RunAsync;

    public async Task<ClipboardImage> ReadPngAsync(CancellationToken ct = default)
    {
        if (await _run("pngpaste", ["-"], maxBytes + 1, ct).ConfigureAwait(false) is { Code: 0, Stdout: { Length: > 0 } png })
        {
            return new ClipboardImage(png);
        }

        var hexLimit = (2 * (maxBytes + 1)) + AppleScriptFraming;
        if (await _run("osascript", ["-e", $"the clipboard as {PngClass}"], hexLimit, ct).ConfigureAwait(false) is { Code: 0, Stdout: { } script }
            && DecodeAppleScriptData(Encoding.UTF8.GetString(script), maxBytes + 1) is { Length: > 0 } decoded)
        {
            return new ClipboardImage(decoded);
        }

        return ClipboardImage.None;
    }

    public async Task<ClipboardImage> ProbePngAsync(CancellationToken ct = default) =>
        await _run("osascript", ["-e", "clipboard info"], MaxInfo, ct).ConfigureAwait(false) is { Code: 0, Truncated: false, Stdout: { } info }
        && Encoding.UTF8.GetString(info).Contains(PngClass, StringComparison.Ordinal)
            ? ClipboardImage.Available
            : ClipboardImage.None;

    public static byte[]? DecodeAppleScriptData(string script, int limit)
    {
        var start = script.IndexOf(DataPrefix, StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        var hex = script.AsSpan(start + DataPrefix.Length);
        var digits = 0;
        while (digits < hex.Length && char.IsAsciiHexDigit(hex[digits]))
        {
            digits++;
        }

        var bytes = Math.Min(digits / 2, limit);
        return bytes == 0 ? null : Convert.FromHexString(hex[..(bytes * 2)]);
    }
}
