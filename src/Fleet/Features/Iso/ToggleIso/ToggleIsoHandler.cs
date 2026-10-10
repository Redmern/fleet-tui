using Fleet.Features.Iso.ToggleIso.Models;
using Fleet.Ports.Settings;
using Fleet.Shared.Iso;
using Fleet.Shared.Iso.Models;

namespace Fleet.Features.Iso.ToggleIso;

public sealed class ToggleIsoHandler(IIsoMode iso)
{
    public const string Usage =
        "usage: fleet iso on | off | status | allow <address> | disallow <address> | code <project> [<code>]";

    public const string OffQuestion =
        "Turn ISO mode off? Status, notices and screens will reach other machines again. Type 'off' to confirm: ";

    public static string? Refusal(IsoCaller caller) =>
        caller.InAgent ? "fleet iso: refused inside an agent; run it yourself in a local terminal."
        : caller.OverSsh ? "fleet iso: refused over ssh; run it in a terminal on this machine."
        : !caller.InteractiveInput ? "fleet iso: refused without an interactive terminal (stdin is not a TTY)."
        : null;

    public IsoReply Handle(IReadOnlyList<string> args, IsoCaller caller, Func<bool> confirmOff)
    {
        if (Refusal(caller) is { } refused)
        {
            return new IsoReply(1, refused);
        }

        var sub = args.Count > 0 ? args[0].Trim().ToLowerInvariant() : "status";
        var value = args.Count > 1 ? args[1].Trim() : string.Empty;
        var config = iso.Load();

        switch (sub)
        {
            case "status":
                return new IsoReply(0, Status(config));

            case "on":
                iso.Save(config with { On = true });
                return new IsoReply(
                    0,
                    "ISO mode is on: other machines get status codes only, and fleet makes no outbound pushes. "
                    + "Agents pick up the push and merge ban when their settings resync.");

            case "off" when !config.On:
                return new IsoReply(0, "ISO mode is already off.");

            case "off":
                if (!confirmOff())
                {
                    return new IsoReply(1, "ISO mode stays on.");
                }

                iso.Save(config with { On = false });
                return new IsoReply(0, "ISO mode is off.");

            case "allow" when value.Length > 0:
                iso.Save(config with
                {
                    AttachFrom = [.. config.AttachFrom.Where(h => !Same(h, value)).Append(value)],
                });
                return new IsoReply(0, $"attach is allowed from {value} while ISO mode is on.");

            case "disallow" when value.Length > 0:
                iso.Save(config with { AttachFrom = [.. config.AttachFrom.Where(h => !Same(h, value))] });
                return new IsoReply(0, $"attach is no longer allowed from {value}.");

            case "code" when value.Length > 0:
                return Code(config, value, args.Count > 2 ? args[2].Trim() : string.Empty);

            default:
                return new IsoReply(2, Usage);
        }
    }

    private IsoReply Code(IsoConfig config, string project, string code)
    {
        var codes = new Dictionary<string, string>(config.Codes, StringComparer.OrdinalIgnoreCase);

        if (code.Length == 0)
        {
            codes.Remove(project);
            iso.Save(config with { Codes = codes });
            return new IsoReply(0, $"{project} gets an automatic code again.");
        }

        if (!IsoCodes.IsValidCode(code))
        {
            return new IsoReply(2, $"'{code}' is not a code: use letters, digits, '-' or '_'.");
        }

        if (codes.Any(c => !Same(c.Key, project) && Same(c.Value, code)))
        {
            return new IsoReply(2, $"'{code}' is already the code of another project.");
        }

        codes[project] = code;
        iso.Save(config with { Codes = codes });
        return new IsoReply(0, $"{project} shows as {code} to other machines.");
    }

    private static string Status(IsoConfig config)
    {
        var lines = new List<string>
        {
            $"ISO mode: {(config.On ? "on" : "off")}",
            "attach allowed from: " + (config.AttachFrom.Count == 0 ? "no host" : string.Join(", ", config.AttachFrom)),
        };

        lines.AddRange(config.Codes
            .OrderBy(c => c.Key, StringComparer.OrdinalIgnoreCase)
            .Select(c => $"code: {c.Key} -> {c.Value}"));

        return string.Join('\n', lines);
    }

    private static bool Same(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
}
