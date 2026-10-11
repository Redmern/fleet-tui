namespace Fleet.Platform.Clipboard;

public sealed record ClipboardTextCopy(string? By)
{
    public bool Done => By is not null;

    public static async Task<ClipboardTextCopy> ThroughAsync(ClipboardText? native, string text) =>
        new(native is null ? null : await native.CopyAsync(text).ConfigureAwait(false));
}
