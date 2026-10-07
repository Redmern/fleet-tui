using Fleet.Ports.Themes;
using Fleet.Ports.Themes.Enums;
using Fleet.Shared.Themes.Models;

namespace Fleet.Platform.Themes;

public sealed class OmarchyThemeSource(string home) : IOmarchy
{
    private const UnixFileMode Executable =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
        | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
        | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;

    public string Root => Path.Combine(home, ".config", "omarchy");

    public string HookFile => Path.Combine(Root, "hooks", "theme-set");

    private string CurrentTheme => Path.Combine(Root, "current", "theme");

    public OmarchySnapshot? Current()
    {
        if (!Directory.Exists(CurrentTheme))
        {
            return null;
        }

        try
        {
            return new OmarchySnapshot(
                Name(),
                Read(Path.Combine(CurrentTheme, "colors.toml")),
                Read(Path.Combine(CurrentTheme, "alacritty.toml")),
                File.Exists(Path.Combine(CurrentTheme, "light.mode")));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public HookInstall InstallHook(string line, string marker)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(HookFile)!);

        HookInstall result;

        if (File.Exists(HookFile))
        {
            var text = File.ReadAllText(HookFile);
            var lines = text.ReplaceLineEndings("\n").Split('\n');

            if (lines.Contains(line))
            {
                return HookInstall.Already;
            }

            var stale = Array.FindIndex(lines, l => l.TrimEnd().EndsWith(marker, StringComparison.Ordinal));

            if (stale >= 0)
            {
                lines[stale] = line;
                File.WriteAllText(HookFile, string.Join('\n', lines));
                result = HookInstall.Updated;
            }
            else
            {
                File.WriteAllText(HookFile, Chain(text, line));
                result = HookInstall.Appended;
            }
        }
        else
        {
            File.WriteAllText(HookFile, $"#!/bin/bash\n{line}\n");
            result = HookInstall.Created;
        }

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(HookFile, File.GetUnixFileMode(HookFile) | Executable);
        }

        return result;
    }

    public static string Chain(string script, string line)
    {
        var lines = script.ReplaceLineEndings("\n").TrimEnd('\n').Split('\n').ToList();
        var last = lines.FindLastIndex(l => l.Trim().Length > 0);
        var exits = last >= 0 && (lines[last].Trim() == "exit" || lines[last].TrimStart().StartsWith("exit ", StringComparison.Ordinal));

        lines.Insert(exits ? last : lines.Count, line);

        return string.Join('\n', lines) + "\n";
    }

    private string Name()
    {
        var named = Read(Path.Combine(Root, "current", "theme.name"))?.Trim();

        if (!string.IsNullOrEmpty(named))
        {
            return named;
        }

        var target = new DirectoryInfo(CurrentTheme).LinkTarget;

        return target is null
            ? "omarchy"
            : Path.GetFileName(target.TrimEnd('/', '\\'));
    }

    private static string? Read(string file) => File.Exists(file) ? File.ReadAllText(file) : null;
}
