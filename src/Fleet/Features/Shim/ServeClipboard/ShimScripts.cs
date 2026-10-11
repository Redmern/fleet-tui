namespace Fleet.Features.Shim.ServeClipboard;

public static class ShimScripts
{
    public const string Verb = "shim";

    public static string Script(string fleetExecutable, string tool) =>
        $"#!/bin/sh\nexec {Quote(fleetExecutable)} {Verb} {tool} \"$@\"\n";

    public static string PathWith(string shimFolder, string? inherited) =>
        string.Join(
            Path.PathSeparator,
            (IEnumerable<string>)[shimFolder, .. Folders(inherited).Where(f => !Same(f, shimFolder))]);

    public static string? RealTool(string tool, string? path, string shimFolder, Func<string, bool> exists) =>
        Folders(path)
            .Where(f => !Same(f, shimFolder))
            .Select(f => Path.Combine(f, tool))
            .FirstOrDefault(exists);

    private static IEnumerable<string> Folders(string? path) =>
        (path ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

    private static bool Same(string a, string b) =>
        string.Equals(a.TrimEnd('/'), b.TrimEnd('/'), StringComparison.Ordinal);

    private static string Quote(string text) => "'" + text.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
}
