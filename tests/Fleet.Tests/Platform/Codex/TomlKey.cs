namespace Fleet.Tests.Platform.Codex;

// A path as a TOML basic-string key; Windows paths carry backslashes that must be escaped.
internal static class TomlKey
{
    public static string Quoted(string path) =>
        $"\"{path.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";
}
