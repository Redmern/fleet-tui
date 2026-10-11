using System.Globalization;
using Fleet.Features.Forwards.OpenPort.Enums;
using Fleet.Features.Forwards.OpenPort.Models;
using Fleet.Ports.Browser;
using Fleet.Ports.Forwards;
using Fleet.Ports.Forwards.Enums;
using Fleet.Ports.Forwards.Models;
using Fleet.Ports.Mux.Exceptions;
using Fleet.Shared.Results;

namespace Fleet.Features.Forwards.OpenPort;

public sealed class OpenPortHandler(IListenerProbe probe, IPortForwards forwards, IBrowserLauncher browser, string user)
{
    public const string Title = "Open port";

    public const string Question = "Port on this machine to open in your browser";

    public static string Waiting(int port) =>
        $"Opening port {port}; a fleet viewing this machine has up to 20 s to report its forward...";

    public static Result<int> Parse(string? typed) =>
        int.TryParse(typed?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var port) && port is >= 1 and <= 65535
            ? Result<int>.Ok(port)
            : Result<int>.Fail($"'{typed}' is not a port; type a whole number in 1-65535.");

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
            if (sent.Result is { } result)
            {
                lines.Add(Describe(viewer, port, result));
                return new OpenPortOutcome(
                    port, listening, OpenPortRoute.Viewer, lines,
                    Failed: result.State is ViewerForwardState.Failed or ViewerForwardState.NoAnswer);
            }

            if (!listening)
            {
                lines.Add($"{viewer} forwards {port} once it listens; open {url} there then.");
            }

            return new OpenPortOutcome(port, listening, OpenPortRoute.Viewer, lines);
        }

        if (sent.SshCommand(port, user) is { } command)
        {
            if (sent.Result is { State: ViewerForwardState.Unsupported, Reason: { } unsupported })
            {
                lines.Add($"{unsupported}; forward it over ssh instead.");
            }

            lines.Add($"Run this on the machine you view fleet from, then open {url} there:");
            return new OpenPortOutcome(port, listening, OpenPortRoute.Ssh, lines, command);
        }

        if (browser.Open(url) is { } failed)
        {
            lines.Add($"could not open {url}: {failed}");
        }

        return new OpenPortOutcome(port, listening, OpenPortRoute.Local, lines);
    }

    private static string Describe(string viewer, int port, ViewerForwardResult result) =>
        result.State switch
        {
            ViewerForwardState.Forwarded => $"{viewer} forwards {port}; it opened {result.Url} in its browser.",
            ViewerForwardState.Pending => $"forward pending: {viewer} forwards {port} once it listens and opens {result.Url} then.",
            ViewerForwardState.BrowserFailed =>
                $"{viewer} forwards {port} but could not open its browser ({result.Reason}); open {result.Url} there.",
            ViewerForwardState.NoAnswer =>
                $"{viewer} did not answer within 20 s{(result.Reason is { } skew ? $" ({skew})" : string.Empty)}; "
                + $"see the fleet.log on {viewer} for what happened. If {viewer} runs an older fleet, restart its fleetd "
                + "there (fleet daemon stop, then fleet attach) and open the port again.",
            _ => $"{viewer} could not forward {port}: {result.Reason ?? "no reason given"}",
        };

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
