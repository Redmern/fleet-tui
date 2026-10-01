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

    public static HookEvent Event(HookPayload payload) =>
        new(
            payload.HookEventName ?? string.Empty,
            payload.Cwd ?? string.Empty,
            payload.SessionId ?? string.Empty,
            payload.TranscriptPath ?? string.Empty,
            payload.AgentId ?? string.Empty,
            payload.NotificationType ?? string.Empty);

    public static string Block(string reason) =>
        JsonSerializer.Serialize(
            new HookBlock { Reason = reason }, HookJsonContext.Default.HookBlock);
}
