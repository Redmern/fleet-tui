using Fleet.Ports.Remotes.Models;

namespace Fleet.Platform.Mux.Embedded.Protocol;

public static class ProjectCreationWire
{
    public const string CreateDirectory = "create_directory";

    public const string Open = "open";

    public const string Repositories = "repositories";

    private const char RepositorySeparator = '\n';

    public static ControlRequest Request(string op, string? host, RemoteProjectRequest creation) =>
        new()
        {
            Op = op,
            Host = host,
            Text = creation.Name,
            Cwd = creation.Path,
            Env = Env(creation),
        };

    public static RemoteProjectRequest Creation(ControlRequest request) =>
        new(
            request.Text ?? string.Empty,
            request.Cwd ?? string.Empty,
            IsSet(request, CreateDirectory, fallback: false),
            IsSet(request, Open, fallback: true),
            RepositoriesIn(request));

    private static Dictionary<string, string> Env(RemoteProjectRequest creation)
    {
        var env = new Dictionary<string, string>
        {
            [CreateDirectory] = Flag(creation.CreateDirectory),
            [Open] = Flag(creation.Open),
        };

        if (creation.Repositories is { Count: > 0 } repositories)
        {
            env[Repositories] = string.Join(RepositorySeparator, repositories);
        }

        return env;
    }

    private static IReadOnlyList<string>? RepositoriesIn(ControlRequest request) =>
        request.Env?.GetValueOrDefault(Repositories)
                ?.Split(RepositorySeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            is { Length: > 0 } repositories
            ? repositories
            : null;

    private static string Flag(bool on) => on ? "true" : "false";

    private static bool IsSet(ControlRequest request, string key, bool fallback) =>
        request.Env?.GetValueOrDefault(key) is { } value
            ? string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
            : fallback;
}
