using System.Globalization;
using Fleet.Features.Forwards.OpenPort.Enums;
using Fleet.Features.Forwards.OpenPort.Models;
using Fleet.Ports.Browser;
using Fleet.Ports.Forwards;
using Fleet.Ports.Forwards.Models;
using Fleet.Ports.Mux.Exceptions;
using Fleet.Shared.Results;

namespace Fleet.Features.Forwards.OpenPort;

public sealed class OpenPortHandler(IListenerProbe probe, IPortForwards forwards, IBrowserLauncher browser, string user)
{
    public const string Title = "Open port";

    public const string Question = "Port on this machine to open in your browser";

    public static Result<int> Parse(string? typed) =>
        int.TryParse(typed?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var port) && port is >= 1 and <= 65535
            ? Result<int>.Ok(port)
            : Result<int>.Fail($"'{typed}' is not a port; type a whole number in 1-65535.");

    public static string? SshCommand(int port, string user, string? sshConnection) =>
        sshConnection?.Split(' ', StringSplitOptions.RemoveEmptyEntries) is [_, _, var host, var sshPort]
            ? $"ssh -N -L {port}:localhost:{port}{(sshPort == "22" ? string.Empty : $" -p {sshPort}")} {user}@{host}"
            : null;

    public async Task<OpenPortOutcome> HandleAsync(int port, CancellationToken ct = default)
    {
        var listening = await probe.ListeningAsync(port, ct).ConfigureAwait(false);
        var lines = new List<string>();

        if (!listening)
        {
            lines.Add($"nothing listens on 127.0.0.1:{port} on this machine yet.");
        }

        var url = $"http://localhost:{port}";
        var sent = await ViewerAsync(port, ct).ConfigureAwait(false);

        if (sent.Viewer is { } viewer)
        {
            if (!listening)
            {
                lines.Add($"{viewer} forwards {port} and opens {url} once it answers.");
            }

            return new OpenPortOutcome(port, listening, OpenPortRoute.Viewer, lines);
        }

        if (SshCommand(port, user, sent.SshConnection) is { } command)
        {
            lines.Add($"Run this on the machine you view fleet from, then open {url} there:");
            return new OpenPortOutcome(port, listening, OpenPortRoute.Ssh, lines, command);
        }

        if (browser.Open(url) is { } failed)
        {
            lines.Add($"could not open {url}: {failed}");
        }

        return new OpenPortOutcome(port, listening, OpenPortRoute.Local, lines);
    }

    private async Task<ViewerForward> ViewerAsync(int port, CancellationToken ct)
    {
        try
        {
            return await forwards.ForwardToViewerAsync(port, ct).ConfigureAwait(false);
        }
        catch (MuxUnavailableException)
        {
            return new ViewerForward(null, null);
        }
    }
}
