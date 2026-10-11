namespace Fleet.Ports;

public interface IClipboardImageSource
{
    Task<ClipboardImage> ReadPngAsync(CancellationToken ct = default);

    Task<ClipboardImage> ProbePngAsync(CancellationToken ct = default);
}

public sealed record ClipboardImage(byte[]? Png, string? Refused = null, bool Listed = false)
{
    public bool HasPng => Listed || Png is { Length: > 0 };

    public static ClipboardImage None { get; } = new((byte[]?)null);

    public static ClipboardImage Available { get; } = new(null, null, Listed: true);

    public static ClipboardImage Refusal(string why) => new(null, why);
}
