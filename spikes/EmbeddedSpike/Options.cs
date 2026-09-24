namespace EmbeddedSpike;

internal sealed record Options(string Program, IReadOnlyList<string> Args, string Prefix, string? DumpPath, string? LogPath, string Keys)
{
    public static Options Parse(string[] argv)
    {
        var prefix = Environment.GetEnvironmentVariable("EMBEDDEDSPIKE_PREFIX") ?? "ctrl+b";
        string? dump = null;
        string? log = Environment.GetEnvironmentVariable("EMBEDDEDSPIKE_LOG");
        var keys = Environment.GetEnvironmentVariable("EMBEDDEDSPIKE_KEYS") ?? "auto";
        var i = 0;

        for (; i < argv.Length; i++)
        {
            switch (argv[i])
            {
                case "--prefix":
                    prefix = argv[++i];
                    break;
                case "--dump":
                    dump = Path.GetFullPath(argv[++i]);
                    break;
                case "--keys":
                    keys = argv[++i];
                    break;
                case "--log":
                    log = Path.GetFullPath(argv[++i]);
                    break;
                case "--":
                    i++;
                    goto done;
                default:
                    goto done;
            }
        }

    done:
        var rest = argv[i..];
        if (rest.Length == 0)
        {
            rest = OperatingSystem.IsWindows()
                ? ["cmd.exe"]
                : [Environment.GetEnvironmentVariable("SHELL") ?? "/bin/sh"];
        }

        return new Options(rest[0], rest[1..], prefix, dump, log, keys);
    }
}
