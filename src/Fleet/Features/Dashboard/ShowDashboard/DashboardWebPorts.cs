using Fleet.Ports.Browser;
using Fleet.Ports.Forwards;
using Fleet.Ports.Forwards.Enums;
using Fleet.Ports.Forwards.Models;
using Fleet.Ports.Projects.Models;

namespace Fleet.Features.Dashboard.ShowDashboard;

public static class DashboardWebPorts
{
    public const string Local = "local";

    public static IReadOnlyList<PortForward> For(IReadOnlyList<PortForward> all, Project project)
    {
        var mine = all
            .Where(f => string.Equals(f.Project, project.Name, StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f.Url is null)
            .ThenBy(f => f.RemotePort)
            .ToList();

        return mine.Count > 0
            ? mine
            : [.. project.Forwarded.Select(p => new PortForward(Local, p, p, ForwardState.Forwarded, project.Name))];
    }

    public static string? Summary(IReadOnlyList<PortForward> ports) =>
        ports.Count == 0
            ? null
            : "web  " + string.Join("  ·  ", ports.Select(p => p.Url is not null
                ? p.LocalPort == p.RemotePort ? $"localhost:{p.LocalPort}" : $"{p.RemotePort} → localhost:{p.LocalPort}"
                : $"{p.RemotePort} {p.State.ToString().ToLowerInvariant()}"));

    public static async Task<string?> OpenAsync(
        IPortForwards forwards, IBrowserLauncher browser, IReadOnlyList<PortForward> ports, CancellationToken ct = default)
    {
        if (ports.FirstOrDefault(p => p.Url is not null) is not { } first)
        {
            return ports.Count == 0
                ? "this project has no web app ports; list them as forwardPorts in its project config"
                : $"no web app port is forwarded yet ({Summary(ports)})";
        }

        return await ForwardOpening.OpenAsync(forwards, browser, first, ct).ConfigureAwait(false) is { } failed
            ? $"could not open {first.Url}: {failed}"
            : $"opened {first.Url}";
    }
}
