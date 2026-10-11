using Fleet.Ports;

namespace Fleet.Platform.Clipboard;

public sealed class NoClipboardImage : IClipboardImageSource
{
    public Task<ClipboardImage> ReadPngAsync(CancellationToken ct = default) => Task.FromResult(ClipboardImage.None);
}
