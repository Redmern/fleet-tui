using Fleet.Cli.Enums;
using Fleet.Cli.Models;

namespace Fleet.Cli;

public static class CommandLine
{
    public const string ProjectFlag = "--project";

    public const string ActionFlag = "--action";

    public const string CallerFlag = "--caller";

    public const string StatusFlag = "--status";

    public const string TitleFlag = Shared.Constants.AgentHarness.TitleFlag;

    public const string VersionFlag = "--version";

    public const string VersionShortFlag = "-v";

    public const string ListFlag = "--list";

    public const string ListShortFlag = "-l";

    private static readonly string[] ValueFlags =
        [ProjectFlag, ActionFlag, CallerFlag, StatusFlag, TitleFlag, VersionFlag, VersionShortFlag];

    private static readonly string[] BoolFlags = [ListFlag, ListShortFlag];

    public static Invocation Parse(IReadOnlyList<string> args)
    {
        var raw = args.Count > 0 ? args[0] : string.Empty;
        var options = args.Count > 1 ? args.Skip(1).ToArray() : [];

        return new Invocation(
            VerbFor(raw),
            raw,
            ValueOf(options, ProjectFlag),
            ValueOf(options, ActionFlag),
            TextOf(options),
            ValueOf(options, CallerFlag),
            ValueOf(options, StatusFlag),
            ValueOf(options, TitleFlag),
            TailOf(options),
            ValueOf(options, VersionFlag, VersionShortFlag),
            HasFlag(options, ListFlag, ListShortFlag));
    }

    private static FleetVerb VerbFor(string verb) => verb switch
    {
        "" => FleetVerb.Pick,
        "dash" => FleetVerb.Dash,
        "menu" => FleetVerb.Menu,
        "request" => FleetVerb.Request,
        "apply-keybinds" => FleetVerb.ApplyKeybinds,
        "setup" => FleetVerb.Setup,
        "dispatch" => FleetVerb.Dispatch,
        "hook-dispatch" => FleetVerb.HookDispatch,
        "report" => FleetVerb.Report,
        "mcp" => FleetVerb.Mcp,
        "quit" => FleetVerb.Quit,
        "doctor" => FleetVerb.Doctor,
        "version" or "--version" or "-v" => FleetVerb.Version,
        "update" => FleetVerb.Update,
        Shared.Constants.AgentHarness.TitledVerb => FleetVerb.Titled,
        "help" or "--help" or "-h" => FleetVerb.Help,
        _ => FleetVerb.Unknown,
    };

    private static string? ValueOf(string[] args, params string[] flags)
    {
        foreach (var flag in flags)
        {
            var i = Array.IndexOf(args, flag);
            var value = i >= 0 && i + 1 < args.Length ? args[i + 1] : null;

            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static bool HasFlag(string[] args, params string[] flags) =>
        flags.Any(args.Contains);

    private static IReadOnlyList<string>? TailOf(string[] args)
    {
        var separator = Array.IndexOf(args, "--");

        return separator >= 0 ? args.Skip(separator + 1).ToList() : null;
    }

    private static string? TextOf(string[] args)
    {
        var separator = Array.IndexOf(args, "--");

        if (separator >= 0)
        {
            var rest = string.Join(' ', args.Skip(separator + 1));

            return string.IsNullOrWhiteSpace(rest) ? null : rest;
        }

        for (var i = 0; i < args.Length; i++)
        {
            if (ValueFlags.Contains(args[i]))
            {
                i++;
                continue;
            }

            if (BoolFlags.Contains(args[i]))
            {
                continue;
            }

            if (!args[i].StartsWith("--", StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(args[i]))
            {
                return args[i];
            }
        }

        return null;
    }
}
