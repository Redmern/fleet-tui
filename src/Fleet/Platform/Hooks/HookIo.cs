using System.Text.Json;
using Fleet.Platform.Hooks.Models;
using Fleet.Shared.Hooks;

namespace Fleet.Platform.Hooks;

public static class HookIo
{
    public static HookPayload Read(TextReader input)
    {
        try
        {
            var text = input.ReadToEnd();

            if (string.IsNullOrWhiteSpace(text))
            {
                return new HookPayload();
            }

            return JsonSerializer.Deserialize(text, HookJsonContext.Default.HookPayload)
                ?? new HookPayload();
        }
        catch (JsonException)
        {
            return new HookPayload();
        }
    }

    public const string ProjectDirVariable = "CLAUDE_PROJECT_DIR";

    public const string MessagingSocketVariable = "CLAUDE_CODE_MESSAGING_SOCKET";

    public const string PeerAddressPrefix = "uds:";

    public static HookEvent Event(HookPayload payload, string? projectDir = null, string? messagingSocket = null) =>
        new(
            payload.HookEventName ?? string.Empty,
            projectDir is { Length: > 0 } started ? started : payload.Cwd ?? string.Empty,
            payload.SessionId ?? string.Empty,
            payload.TranscriptPath ?? string.Empty,
            payload.AgentId ?? string.Empty,
            payload.NotificationType ?? string.Empty,
            payload.Source ?? string.Empty,
            PeerAddress(messagingSocket));

    public static string PeerAddress(string? messagingSocket) =>
        messagingSocket?.Trim() is { Length: > 0 } socket
            ? socket.StartsWith(PeerAddressPrefix, StringComparison.Ordinal) ? socket : PeerAddressPrefix + socket
            : string.Empty;

    public static string Block(string reason) =>
        JsonSerializer.Serialize(
            new HookBlock { Reason = reason }, HookJsonContext.Default.HookBlock);
}
