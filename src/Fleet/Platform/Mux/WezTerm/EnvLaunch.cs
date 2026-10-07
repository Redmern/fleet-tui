using Fleet.Shared.Constants;

namespace Fleet.Platform.Mux.WezTerm;

public static class EnvLaunch
{
    public static IReadOnlyList<string> Wrap(
        bool windows, IReadOnlyDictionary<string, string> env, IReadOnlyList<string> command)
    {
        if (command.Any(NeedsQuoting))
        {
            return
            [
                Environment.ProcessPath ?? "fleet",
                AgentHarness.WithEnvVerb,
                .. env.Select(kv => $"{kv.Key}={kv.Value}"),
                "--",
                .. command,
            ];
        }

        var run = string.Join(' ', command);

        if (windows)
        {
            var parts = env
                .Select(kv => $"set {kv.Key}={kv.Value}")
                .Append(run);

            return ["cmd", "/c", string.Join("& ", parts)];
        }

        var lines = env
            .Select(kv => kv.Value.Length == 0 ? $"unset {kv.Key}" : $"export {kv.Key}={kv.Value}")
            .Append($"exec {run}");

        return ["sh", "-c", string.Join("; ", lines)];
    }

    private static bool NeedsQuoting(string arg) =>
        arg.Length == 0
        || arg.Any(c => char.IsWhiteSpace(c) || c is '"' or '\'' or '&' or '|' or '<' or '>' or '^' or '%' or ';' or '$' or '`' or '(' or ')' or '*' or '?');
}
