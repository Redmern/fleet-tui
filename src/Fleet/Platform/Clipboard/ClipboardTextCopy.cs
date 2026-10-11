namespace Fleet.Platform.Clipboard;

public sealed record ClipboardTextCopy(ClipboardTextResult Result, string? By = null)
{
    public static readonly ClipboardTextCopy NotSet = new(ClipboardTextResult.NotSet);
    public static readonly ClipboardTextCopy TooLarge = new(ClipboardTextResult.TooLarge);
    public static readonly ClipboardTextCopy Superseded = new(ClipboardTextResult.Superseded);

    public bool Done => Result == ClipboardTextResult.Set;
}
