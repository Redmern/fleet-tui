using Fleet.Ports.Mux.Models;

namespace Fleet.Features.Files.BrowseFiles;

public static class FileBrowser
{
    public const string Command = "yazi";

    public const string CwdFlag = "--cwd-file";

    public static SpawnOptions Browse(string cwd, string project, string? window) =>
        new()
        {
            Cwd = cwd,
            SessionName = project,
            WindowId = window,
            Args = [Command],
        };

    public static SpawnOptions Choose(string cwd, string project, string? window, string cwdFile) =>
        new()
        {
            Cwd = cwd,
            SessionName = project,
            WindowId = window,
            Args = [Command, CwdFlag, cwdFile],
        };

    public static string? Chosen(string cwdFile, Func<string, string?> read)
    {
        var text = read(cwdFile);

        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var first = text
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();

        return string.IsNullOrWhiteSpace(first) ? null : first;
    }

    public static string StartIn(string wanted, Func<string, bool> exists, string fallback)
    {
        if (wanted.Length > 0 && exists(wanted))
        {
            return wanted;
        }

        var parent = wanted.Length > 0 ? Path.GetDirectoryName(wanted) : null;

        return parent is { Length: > 0 } && exists(parent) ? parent : fallback;
    }
}
