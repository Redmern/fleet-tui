namespace Fleet.Platform.Mux.WezTerm;

public static class EnvLaunch
{
    public static IReadOnlyList<string> Wrap(
        bool windows, IReadOnlyDictionary<string, string> env, IReadOnlyList<string> command)
    {
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
}
