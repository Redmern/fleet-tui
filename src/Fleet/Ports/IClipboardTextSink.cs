namespace Fleet.Ports;

public interface IClipboardTextSink
{
    Task<ClipboardCopy> CopyAsync(string text, CancellationToken ct = default);
}

public sealed record ClipboardCopy(string? Refused = null)
{
    public const int MaxBytes = 1024 * 1024;

    public static ClipboardCopy Done { get; } = new();

    public static ClipboardCopy Refusal(string why) => new(why);
}
