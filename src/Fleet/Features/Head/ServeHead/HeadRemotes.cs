using System.Globalization;
using Fleet.Features.Head.ServeHead.Models;
using Fleet.Ports.Mcp.Models;
using Fleet.Ports.Mux.Exceptions;
using Fleet.Ports.Remotes.Enums;
using Fleet.Ports.Remotes.Models;

namespace Fleet.Features.Head.ServeHead;

public sealed class HeadRemotes(HeadDeps deps, HeadTiming timing)
{
    public static bool IsLocal(string remote) =>
        remote.Length == 0 || string.Equals(remote, HeadTools.Local, StringComparison.OrdinalIgnoreCase);

    public async Task<McpResult> ListAsync(CancellationToken ct)
    {
        var live = await deps.Remotes.ListAsync(ct).ConfigureAwait(false);
        var known = deps.KnownRemotes.Load();
        var lines = new List<string> { $"{HeadTools.Local}  the machine fleet was opened on; the head runs here" };

        foreach (var remote in known.OrderBy(k => k.Nickname ?? k.Host, StringComparer.OrdinalIgnoreCase))
        {
            var machine = live.FirstOrDefault(m => SameHost(m.Host, remote.Host));
            var name = remote.Nickname ?? "(no nickname)";

            lines.Add($"{name}  {remote.Host}  {State(machine)}  last connected {When(remote.LastConnected)}"
                + (remote.Nickname is null ? "  · give it a nickname under Remote machines to reach it here" : string.Empty));
        }

        lines.AddRange(live
            .Where(m => !known.Any(k => SameHost(k.Host, m.Host)))
            .Select(m => $"(no nickname)  {m.Host}  {State(m)}  · give it a nickname under Remote machines to reach it here"));

        return McpResult.Ok(string.Join('\n', lines));
    }

    public async Task<McpResult> HandleAsync(string nickname, McpRequest request, CancellationToken ct)
    {
        var matches = deps.KnownRemotes.Load()
            .Where(k => string.Equals(k.Nickname, nickname, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matches.Count == 0)
        {
            return McpResult.Error(
                $"no remote machine is nicknamed '{nickname}'. Machines: "
                + string.Join(", ", [HeadTools.Local, .. Nicknames()]) + ".");
        }

        if (matches.Count > 1)
        {
            return McpResult.Error(
                $"more than one remote machine is nicknamed '{nickname}' ({string.Join(", ", matches.Select(m => m.Host))}); "
                + "rename one under Remote machines.");
        }

        var remote = matches[0];
        var (machine, failed) = await ConnectedAsync(remote, ct).ConfigureAwait(false);

        if (failed is not null)
        {
            return McpResult.Error(failed);
        }

        var label = remote.Nickname!;

        if (request.Tool == HeadTools.ListProjects)
        {
            return ListProjects(machine!, label);
        }

        var name = request.Value(HeadTools.Project).Trim();
        string? project = null;

        if (name.Length > 0)
        {
            project = machine!.Projects.FirstOrDefault(p => string.Equals(p, name, StringComparison.OrdinalIgnoreCase));

            if (project is null)
            {
                return McpResult.Error(
                    $"no project named '{name}' on {label}. Projects there: "
                    + (machine.Projects.Count == 0 ? "none" : string.Join(", ", machine.Projects)) + ".");
            }
        }
        else if (request.Tool != HeadTools.ListAgents)
        {
            return McpResult.Error($"'{HeadTools.Project}' is required for {request.Tool}.");
        }

        return request.Tool switch
        {
            HeadTools.SwitchProject => await ShowAsync(remote.Host, project!, $"switched to {project} on {label}.", ct)
                .ConfigureAwait(false),
            HeadTools.MenuAction => await MenuAsync(remote.Host, label, project!, request, ct).ConfigureAwait(false),
            _ => On(label, await deps.Remotes.HeadAsync(remote.Host, Forwarded(request, project), ct).ConfigureAwait(false)),
        };
    }

    private async Task<McpResult> MenuAsync(string host, string label, string project, McpRequest request, CancellationToken ct)
    {
        if (HeadService.MenuActionError(request) is { } invalid)
        {
            return McpResult.Error(invalid);
        }

        var asked = await deps.Remotes.HeadAsync(host, Forwarded(request, project), ct).ConfigureAwait(false);

        return asked.IsError
            ? On(label, asked)
            : await ShowAsync(host, project, $"on {label}: {asked.Text}", ct).ConfigureAwait(false);
    }

    private async Task<McpResult> ShowAsync(string host, string project, string done, CancellationToken ct)
    {
        try
        {
            await deps.Remotes.ShowHereAsync(host, project, ct).ConfigureAwait(false);
            return McpResult.Ok(done);
        }
        catch (MuxUnavailableException e)
        {
            return McpResult.Error($"could not show {project}: {e.Message}");
        }
    }

    private static McpRequest Forwarded(McpRequest request, string? project)
    {
        var arguments = request.Arguments
            .Where(a => a.Key != HeadTools.Remote)
            .ToDictionary(a => a.Key, a => a.Value);

        if (project is not null)
        {
            arguments[HeadTools.Project] = project;
        }

        return request with { Arguments = arguments };
    }

    private static McpResult On(string label, McpResult result) =>
        result with { Text = $"on {label}: {result.Text}" };

    private static McpResult ListProjects(RemoteMachine machine, string label) =>
        machine.Projects.Count == 0
            ? McpResult.Ok($"{label} has no fleet projects.")
            : McpResult.Ok(string.Join('\n', machine.Projects
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .Select(p => $"{p}  {(machine.IsRunning(p) ? "open" : "closed")}  on {label}")));

    private async Task<(RemoteMachine? Machine, string? Failed)> ConnectedAsync(KnownRemote remote, CancellationToken ct)
    {
        var label = $"{remote.Nickname} ({remote.Host})";
        var waited = System.Diagnostics.Stopwatch.StartNew();
        var asked = false;

        while (true)
        {
            RemoteMachine? machine;

            try
            {
                machine = (await deps.Remotes.ListAsync(ct).ConfigureAwait(false))
                    .FirstOrDefault(m => SameHost(m.Host, remote.Host));

                if (machine is null or { State: RemoteState.Failed } && !asked)
                {
                    await deps.Remotes.ConnectAsync(remote.Host, ct).ConfigureAwait(false);
                    asked = true;
                    continue;
                }
            }
            catch (MuxUnavailableException e)
            {
                return (null, $"could not connect to {label}: {e.Message}");
            }

            switch (machine)
            {
                case { State: RemoteState.Connected }:
                    deps.KnownRemotes.Remember(machine.Host, DateTimeOffset.Now);
                    return (machine, null);

                case { State: RemoteState.Failed }:
                    return (null, $"could not reach {label} over ssh: {machine.Error ?? "the connection failed"}.");

                case { State: RemoteState.Asking }:
                    return (null,
                        $"ssh to {label} is asking \"{machine.Prompt?.Trim()}\"; answer it under Remote machines in fleet, "
                        + "then try again.");

                case null:
                    return (null, $"fleet could not start a connection to {label}.");
            }

            if (waited.Elapsed > timing.ConnectTimeout)
            {
                return (null, $"{label} is still connecting after {timing.ConnectTimeout.TotalSeconds:0}s; try again in a moment.");
            }

            await Task.Delay(timing.Poll, ct).ConfigureAwait(false);
        }
    }

    private IEnumerable<string> Nicknames() =>
        deps.KnownRemotes.Load()
            .Select(k => k.Nickname)
            .OfType<string>()
            .Order(StringComparer.OrdinalIgnoreCase);

    private static string State(RemoteMachine? machine) =>
        machine?.State switch
        {
            RemoteState.Connected => "connected",
            RemoteState.Asking => "connecting · ssh is asking a question in Remote machines",
            RemoteState.Failed => "failed" + (machine.Error is { Length: > 0 } error ? $": {error}" : string.Empty),
            RemoteState.Connecting => "connecting",
            _ => "not connected",
        };

    private static string When(DateTimeOffset at) =>
        at == default ? "never" : at.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    private static bool SameHost(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
