using System.Globalization;
using Fleet.Features.Forwards.OpenPort.Models;
using Fleet.Ports.Browser;
using Fleet.Ports.Forwards;
using Fleet.Shared.Results;

namespace Fleet.Features.Forwards.OpenPort;

public sealed class OpenPortHandler(IListenerProbe probe, IBrowserLauncher browser)
{
    public const string Title = "Open port";

    public const string Question = "Port on this machine to open in your browser";

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
            lines.Add($"nothing listens on 127.0.0.1:{port} on this machine yet; opening it anyway.");
        }

        var url = $"http://localhost:{port}";

        if (browser.Open(url) is { } failed)
        {
            lines.Add($"could not open {url}: {failed}");
        }

        return new OpenPortOutcome(port, listening, lines);
    }
}
