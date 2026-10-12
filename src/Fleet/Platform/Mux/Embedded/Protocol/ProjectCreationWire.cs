using Fleet.Ports.Remotes.Models;

namespace Fleet.Platform.Mux.Embedded.Protocol;

public static class ProjectCreationWire
{
    public const string CreateDirectory = "create_directory";

    public const string Open = "open";

    public static ControlRequest Request(string op, string? host, RemoteProjectRequest creation) =>
        new()
        {
            Op = op,
            Host = host,
            Text = creation.Name,
            Cwd = creation.Path,
            Env = new Dictionary<string, string>
            {
                [CreateDirectory] = Flag(creation.CreateDirectory),
                [Open] = Flag(creation.Open),
            },
        };

    public static RemoteProjectRequest Creation(ControlRequest request) =>
        new(
            request.Text ?? string.Empty,
            request.Cwd ?? string.Empty,
            IsSet(request, CreateDirectory, fallback: false),
            IsSet(request, Open, fallback: true));

    private static string Flag(bool on) => on ? "true" : "false";

    private static bool IsSet(ControlRequest request, string key, bool fallback) =>
        request.Env?.GetValueOrDefault(key) is { } value
            ? string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
            : fallback;
}
