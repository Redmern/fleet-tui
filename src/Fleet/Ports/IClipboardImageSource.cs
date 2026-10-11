namespace Fleet.Ports;

public interface IClipboardImageSource
{
    Task<ClipboardImage> ReadPngAsync(CancellationToken ct = default);
}

public sealed record ClipboardImage(byte[]? Png, string? Refused = null)
{
    public static ClipboardImage None { get; } = new((byte[]?)null);

    public static ClipboardImage Refusal(string why) => new(null, why);
}
