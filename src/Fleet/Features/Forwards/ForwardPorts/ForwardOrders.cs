using System.Globalization;
using Fleet.Features.Forwards.ForwardPorts.Enums;
using Fleet.Features.Forwards.ForwardPorts.Models;
using Fleet.Shared.Results;

namespace Fleet.Features.Forwards.ForwardPorts;

public static class ForwardOrders
{
    public const string LocalFlag = "--local";

    public const string OpenFlag = "--open";

    public const string Usage =
        "usage: fleet forward <host> <remotePort> [--local <port>] [--open]\n"
        + "       fleet forward ls\n"
        + "       fleet forward rm <host> <remotePort>\n"
        + "       fleet forward open <port> [<host>]\n"
        + "       fleet forward start <host> <project> [--open]\n"
        + "       fleet forward stop <host> <project>";

    public static Result<ForwardOrder> Parse(IReadOnlyList<string> args)
    {
        var open = args.Contains(OpenFlag);
        var local = Value(args, LocalFlag);
        var words = Words(args);

        if (local is not null && Port(local) is null)
        {
            return Result<ForwardOrder>.Fail($"{local} is not a port\n{Usage}");
        }

        return words switch
        {
        [] or ["ls" or "list"] => Result<ForwardOrder>.Ok(new ForwardOrder(ForwardVerb.List)),
            ["rm" or "remove", var host, var port] when Port(port) is { } remote =>
                Result<ForwardOrder>.Ok(new ForwardOrder(ForwardVerb.Remove, host, remote)),
            ["open", var port] when Port(port) is { } remote => Result<ForwardOrder>.Ok(new ForwardOrder(ForwardVerb.Open, Port: remote)),
            ["open", var port, var host] when Port(port) is { } remote =>
                Result<ForwardOrder>.Ok(new ForwardOrder(ForwardVerb.Open, host, remote)),
            ["start", var host, var project] => Result<ForwardOrder>.Ok(new ForwardOrder(ForwardVerb.Start, host, Project: project, Open: open)),
            ["stop", var host, var project] => Result<ForwardOrder>.Ok(new ForwardOrder(ForwardVerb.Stop, host, Project: project)),
            [var host, var port] when Port(port) is { } remote =>
                Result<ForwardOrder>.Ok(new ForwardOrder(ForwardVerb.Add, host, remote, local is null ? null : Port(local), Open: open)),
            _ => Result<ForwardOrder>.Fail(Usage),
        };
    }

    private static string? Value(IReadOnlyList<string> args, string flag)
    {
        var at = args.ToList().IndexOf(flag);
        return at >= 0 && at + 1 < args.Count ? args[at + 1] : null;
    }

    private static List<string> Words(IReadOnlyList<string> args)
    {
        var words = new List<string>();
        for (var i = 0; i < args.Count; i++)
        {
            if (args[i] == LocalFlag)
            {
                i++;
                continue;
            }

            if (!args[i].StartsWith("--", StringComparison.Ordinal))
            {
                words.Add(args[i]);
            }
        }

        return words;
    }

    private static int? Port(string text) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var port) && port is > 0 and <= 65535 ? port : null;
}
