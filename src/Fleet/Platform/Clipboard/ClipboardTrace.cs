using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Fleet.Shared.Constants;

namespace Fleet.Platform.Clipboard;

public sealed class ClipboardTrace(string? path, TimeProvider? clock = null)
{
    private static readonly Lock Gate = new();

    public static ClipboardTrace Off { get; } = new(null);

    public static ClipboardTrace FromEnvironment() =>
        new(Environment.GetEnvironmentVariable(AgentHarness.ClipboardTraceVariable) is { Length: > 0 } path ? path : null);

    public bool On => path is not null;

    public static string Sha256(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    public void Write(string hop, string? text, string? pane = null, string? detail = null)
    {
        if (path is null)
        {
            return;
        }

        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteNumber("ts", (clock ?? TimeProvider.System).GetUtcNow().ToUnixTimeMilliseconds());
            json.WriteString("hop", hop);
            if (text is not null)
            {
                json.WriteString("sha256", Sha256(text));
                json.WriteNumber("bytes", Encoding.UTF8.GetByteCount(text));
            }

            if (pane is not null)
            {
                json.WriteString("pane", pane);
            }

            if (detail is not null)
            {
                json.WriteString("detail", detail);
            }

            json.WriteEndObject();
        }

        buffer.WriteByte((byte)'\n');
        lock (Gate)
        {
            try
            {
                using var file = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                file.Write(buffer.GetBuffer(), 0, (int)buffer.Length);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
