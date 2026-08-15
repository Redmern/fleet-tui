using Fleet.Cli.Enums;
using Fleet.Cli.Models;

namespace Fleet.Cli;

public static class CommandLine
{
    public const string ProjectFlag = "--project";

    public const string ActionFlag = "--action";

    public const string CallerFlag = "--caller";

    private static readonly string[] ValueFlags = [ProjectFlag, ActionFlag, CallerFlag];

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
            ValueOf(options, CallerFlag));
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
        "quit" => FleetVerb.Quit,
        "doctor" => FleetVerb.Doctor,
        "help" or "--help" or "-h" => FleetVerb.Help,
        _ => FleetVerb.Unknown,
    };

    private static string? ValueOf(string[] args, string flag)
    {
        var i = Array.IndexOf(args, flag);
        var value = i >= 0 && i + 1 < args.Length ? args[i + 1] : null;

        return string.IsNullOrWhiteSpace(value) ? null : value;
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

            if (!args[i].StartsWith("--", StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(args[i]))
            {
                return args[i];
            }
        }

        return null;
    }
}
