using System.Globalization;
using Fleet.Features.Head.ServeHead.Models;
using Fleet.Ports.Forwards;
using Fleet.Ports.Forwards.Models;
using Fleet.Ports.Mcp.Models;
using Fleet.Ports.Mux.Exceptions;

namespace Fleet.Features.Head.ServeHead;

public sealed class HeadForwards(HeadDeps deps)
{
    public async Task<McpResult> HandleAsync(McpRequest request, CancellationToken ct)
    {
        if (deps.Forwards is not { } forwards || deps.Browser is not { } browser)
        {
            return McpResult.Error("this fleet cannot forward ports.");
        }

        try
        {
            return request.Tool switch
            {
                HeadTools.ListForwards => List(await forwards.ListAsync(ct).ConfigureAwait(false)),
                HeadTools.ForwardPort when OnThisMachine(request) => Mcp(
                    (await forwards.ForwardToViewerAsync(Port(request, HeadTools.Port), ct).ConfigureAwait(false))
                        .Answer(Port(request, HeadTools.Port), forward: true, deps.User)),
                HeadTools.ForwardPort => await WithHostAsync(request, async host => Done(
                    await forwards.AddAsync(host, Port(request, HeadTools.Port), LocalPort(request), ct).ConfigureAwait(false)))
                    .ConfigureAwait(false),
                HeadTools.UnforwardPort when OnThisMachine(request) => Mcp(
                    (await forwards.UnforwardFromViewerAsync(Port(request, HeadTools.Port), ct).ConfigureAwait(false))
                        .Answer(Port(request, HeadTools.Port), forward: false, deps.User)),
                HeadTools.UnforwardPort => await WithHostAsync(request, async host =>
                {
                    await forwards.RemoveAsync(host, Port(request, HeadTools.Port), ct).ConfigureAwait(false);
                    return McpResult.Ok($"stopped forwarding {Port(request, HeadTools.Port)} from {host}.");
                }).ConfigureAwait(false),
                HeadTools.OpenUrl => await OpenAsync(forwards, browser, request, ct).ConfigureAwait(false),
                HeadTools.StartStack => await WithHostAsync(request, async host =>
                {
                    var up = await forwards.StartStackAsync(host, Project(request), ct).ConfigureAwait(false);
                    return request.Flag(HeadTools.Open) && await ForwardOpening.OpenAsync(forwards, browser, up, ct).ConfigureAwait(false) is { } failed
                        ? McpResult.Error($"{up.Describe()}; could not open the browser: {failed}")
                        : Done(up);
                }).ConfigureAwait(false),
                HeadTools.StopStack => await WithHostAsync(request, async host =>
                {
                    await forwards.StopStackAsync(host, Project(request), ct).ConfigureAwait(false);
                    return McpResult.Ok($"stopped {Project(request)} on {host}.");
                }).ConfigureAwait(false),
                _ => McpResult.Error($"the head has no tool named '{request.Tool}'."),
            };
        }
        catch (Exception e) when (e is MuxUnavailableException or ArgumentException)
        {
            return McpResult.Error(e.Message);
        }
    }

    private async Task<McpResult> OpenAsync(
        IPortForwards forwards, Ports.Browser.IBrowserLauncher browser, McpRequest request, CancellationToken ct)
    {
        var port = Port(request, HeadTools.Port);
        var remote = request.Value(HeadTools.Remote).Trim();
        var host = remote.Length == 0 ? null : HostOf(remote);
        var row = ForwardOpening.Find(await forwards.ListAsync(ct).ConfigureAwait(false), port, host);

        if (row is null)
        {
            return McpResult.Error($"port {port} is not forwarded{(host is null ? string.Empty : $" from {host}")}; forward it with {HeadTools.ForwardPort} first.");
        }

        return await ForwardOpening.OpenAsync(forwards, browser, row, ct).ConfigureAwait(false) is { } failed
            ? McpResult.Error($"could not open {row.Url}: {failed}")
            : McpResult.Ok($"opened {row.Url} ({row.Host} port {row.RemotePort}).");
    }

    private async Task<McpResult> WithHostAsync(McpRequest request, Func<string, Task<McpResult>> act)
    {
        var remote = request.Value(HeadTools.Remote).Trim();
        if (HeadRemotes.IsLocal(remote))
        {
            return McpResult.Error($"'{HeadTools.Remote}' must name a remote machine; ports of {HeadTools.Local} are already on localhost.");
        }

        return await act(HostOf(remote)).ConfigureAwait(false);
    }

    private static bool OnThisMachine(McpRequest request) => HeadRemotes.IsLocal(request.Value(HeadTools.Remote).Trim());

    private static McpResult Mcp(Shared.Results.Result<string> answer) =>
        answer.Succeeded ? McpResult.Ok(answer.Value) : McpResult.Error(answer.Error!);

    private string HostOf(string remote) =>
        deps.KnownRemotes.Load().FirstOrDefault(k => string.Equals(k.Nickname, remote, StringComparison.OrdinalIgnoreCase))?.Host
        ?? remote;

    private static McpResult List(IReadOnlyList<PortForward> rows) =>
        McpResult.Ok(rows.Count == 0
            ? "no forwarded or detected ports; connect a remote machine whose projects listen on ports."
            : string.Join('\n', rows.Select(r => r.Describe() + (r.Url is { } url ? $"  {url}" : string.Empty))));

    private static McpResult Done(PortForward row) =>
        row.State == Ports.Forwards.Enums.ForwardState.Failed
            ? McpResult.Error(row.Describe())
            : McpResult.Ok(row.Describe() + (row.Url is { } url ? $"  {url}" : string.Empty));

    private static int Port(McpRequest request, string key) =>
        int.TryParse(request.Value(key).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var port) && port is > 0 and <= 65535
            ? port
            : throw new ArgumentException($"'{key}' must be a port number from 1 to 65535.");

    private static int? LocalPort(McpRequest request) =>
        request.Value(HeadTools.LocalPort).Trim().Length == 0 ? null : Port(request, HeadTools.LocalPort);

    private static string Project(McpRequest request) =>
        request.Value(HeadTools.Project).Trim() is { Length: > 0 } project
            ? project
            : throw new ArgumentException($"'{HeadTools.Project}' is required for {request.Tool}.");
}
