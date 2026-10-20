using System.Text;
using System.Text.Json;

namespace Fleet.Platform.Clipboard;

public sealed class ClipboardSink(string path, TimeProvider? clock = null)
{
    public const string Variable = "FLEET_CLIPBOARD_SINK";

    private static readonly Lock Gate = new();

    public static ClipboardSink? FromEnvironment() =>
        Environment.GetEnvironmentVariable(Variable) is { Length: > 0 } path ? new ClipboardSink(path) : null;

    public string Path => path;

    public string? Append(string text)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteNumber("ts", (clock ?? TimeProvider.System).GetUtcNow().ToUnixTimeMilliseconds());
            json.WriteNumber("chars", text.Length);
            json.WriteNumber("bytes", Encoding.UTF8.GetByteCount(text));
            json.WriteString("sha256", ClipboardTrace.Sha256(text));
            json.WriteString("text", text);
            json.WriteEndObject();
        }

        buffer.WriteByte((byte)'\n');
        lock (Gate)
        {
            try
            {
                using var file = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                file.Write(buffer.GetBuffer(), 0, (int)buffer.Length);
                return null;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return e.Message;
            }
        }
    }
}
