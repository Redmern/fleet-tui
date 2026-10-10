using System.Globalization;
using Fleet.Features.Forwards.ForwardPorts.Enums;
using Fleet.Features.Forwards.ForwardPorts.Models;
using Fleet.Ports.Mcp.Models;
using Fleet.Shared.Results;

namespace Fleet.Features.Forwards.ForwardPorts;

public static class McpForwards
{
    public const string Remote = "remote";

    public const string Port = "port";

    public const string LocalPort = "local_port";

    public const string Project = "project";

    public const string Open = "open";

    public static Result<ForwardOrder> Order(McpRequest request, ForwardVerb verb)
    {
        var remote = request.Value(Remote).Trim();
        var project = request.Value(Project).Trim();
        var port = Number(request.Value(Port));
        var local = Number(request.Value(LocalPort));

        string? missing = verb switch
        {
            ForwardVerb.Add or ForwardVerb.Remove when remote.Length == 0 => Remote,
            ForwardVerb.Add or ForwardVerb.Remove or ForwardVerb.Open when port is null => Port,
            ForwardVerb.Start or ForwardVerb.Stop when remote.Length == 0 => Remote,
            ForwardVerb.Start or ForwardVerb.Stop when project.Length == 0 => Project,
            _ => null,
        };

        if (missing is not null)
        {
            return Result<ForwardOrder>.Fail($"'{missing}' is required for {request.Tool}{(missing is Port or LocalPort ? " (1 to 65535)" : string.Empty)}.");
        }

        if (request.Value(LocalPort).Trim().Length > 0 && local is null)
        {
            return Result<ForwardOrder>.Fail($"'{LocalPort}' must be a port from 1 to 65535.");
        }

        return Result<ForwardOrder>.Ok(new ForwardOrder(
            verb,
            remote.Length == 0 ? null : remote,
            port ?? 0,
            local,
            project.Length == 0 ? null : project,
            request.Flag(Open)));
    }

    private static int? Number(string text) =>
        int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var port) && port is > 0 and <= 65535 ? port : null;
}
