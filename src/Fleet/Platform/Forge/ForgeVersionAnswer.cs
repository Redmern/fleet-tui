using System.Net;
using System.Text.Json;
using Fleet.Ports.Forge.Enums;

namespace Fleet.Platform.Forge;

public static class ForgeVersionAnswer
{
    public const string Path = "/api/v1/version";

    public const string SignInRequired = "Only signed in user is allowed to call APIs.";

    public static ForgeProbeResult Read(HttpStatusCode status, string body)
    {
        if (status == HttpStatusCode.OK)
        {
            return Text(body, "version").Length > 0 ? ForgeProbeResult.ForgejoOrGitea : ForgeProbeResult.NotAForge;
        }

        if (status is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
        {
            return string.Equals(Text(body, "message"), SignInRequired, StringComparison.Ordinal)
                ? ForgeProbeResult.ForgejoOrGitea
                : ForgeProbeResult.NotAForge;
        }

        return status == HttpStatusCode.NotFound ? ForgeProbeResult.NotAForge : ForgeProbeResult.Unreachable;
    }

    private static string Text(string body, string property)
    {
        try
        {
            using var document = JsonDocument.Parse(body);

            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty(property, out var value)
                && value.ValueKind == JsonValueKind.String
                    ? (value.GetString() ?? string.Empty).Trim()
                    : string.Empty;
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }
}
