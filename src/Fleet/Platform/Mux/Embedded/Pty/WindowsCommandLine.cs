using System.Text;

namespace Fleet.Platform.Mux.Embedded.Pty;

public static class WindowsCommandLine
{
    public static string For(string program, IReadOnlyList<string> args) =>
        For(program, args, Resolve);

    public static string For(string program, IReadOnlyList<string> args, Func<string, string> resolve)
    {
        var resolved = resolve(program);
        var ext = Path.GetExtension(resolved);

        if (Path.GetFileName(resolved).Equals("cmd.exe", StringComparison.OrdinalIgnoreCase)
            || Path.GetFileName(resolved).Equals("cmd", StringComparison.OrdinalIgnoreCase))
        {
            var run = args.ToList().FindIndex(a => a.Equals("/c", StringComparison.OrdinalIgnoreCase)
                                                   || a.Equals("/k", StringComparison.OrdinalIgnoreCase));
            if (run >= 0)
            {
                var before = args.Take(run + 1).Select(Quote);
                var after = string.Join(' ', args.Skip(run + 1));
                return string.Join(' ', new[] { Quote(resolved) }.Concat(before).Append(after));
            }
        }

        if (ext.Equals(".cmd", StringComparison.OrdinalIgnoreCase) || ext.Equals(".bat", StringComparison.OrdinalIgnoreCase))
        {
            var inner = string.Join(' ', new[] { Quote(resolved) }.Concat(args.Select(Quote)));
            return $"{Quote(Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe")} /d /c \"{inner}\"";
        }

        return string.Join(' ', new[] { Quote(resolved) }.Concat(args.Select(Quote)));
    }

    public static string Quote(string arg)
    {
        if (arg.Length > 0 && arg.IndexOfAny([' ', '\t', '"', '\n', '\v']) < 0)
        {
            return arg;
        }

        var quoted = new StringBuilder("\"");
        var backslashes = 0;

        foreach (var c in arg)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }

            if (c == '"')
            {
                quoted.Append('\\', backslashes * 2 + 1).Append('"');
            }
            else
            {
                quoted.Append('\\', backslashes).Append(c);
            }

            backslashes = 0;
        }

        return quoted.Append('\\', backslashes * 2).Append('"').ToString();
    }

    public static string Resolve(string program)
    {
        if (Path.IsPathRooted(program) || program.Contains(Path.DirectorySeparatorChar) || program.Contains('/'))
        {
            return program;
        }

        var extensions = Path.HasExtension(program)
            ? [string.Empty]
            : (Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD")
                .Split(';', StringSplitOptions.RemoveEmptyEntries);
        var dirs = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        foreach (var dir in dirs)
        {
            foreach (var ext in extensions)
            {
                try
                {
                    var candidate = Path.Combine(dir, program + ext.ToLowerInvariant());
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
                catch (ArgumentException)
                {
                }
            }
        }

        return program;
    }
}
