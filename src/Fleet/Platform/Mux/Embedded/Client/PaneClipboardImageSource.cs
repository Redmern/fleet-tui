using Fleet.Platform.Mux.Embedded.Daemon;
using Fleet.Platform.Mux.Embedded.Protocol;
using Fleet.Ports;
using Fleet.Shared.Constants;

namespace Fleet.Platform.Mux.Embedded.Client;

public sealed class PaneClipboardImageSource(Endpoint endpoint, string? pane, string? token) : IClipboardImageSource
{
    public static readonly TimeSpan ConnectWithin = TimeSpan.FromSeconds(3);

    public static readonly TimeSpan AnswerWithin = FleetDaemon.ClipboardImageWithin + TimeSpan.FromSeconds(2);

    public Task<ClipboardImage> ReadPngAsync(CancellationToken ct = default) => AskAsync(FleetDaemon.ClipboardImageOp, ct);

    public Task<ClipboardImage> ProbePngAsync(CancellationToken ct = default) => AskAsync(FleetDaemon.ClipboardTypesOp, ct);

    private async Task<ClipboardImage> AskAsync(string op, CancellationToken ct)
    {
        if (pane is not { Length: > 0 } caller)
        {
            return ClipboardImage.Refusal($"not in a fleet pane ({FleetDaemon.PaneVariable} is not set)");
        }

        try
        {
            using var wire = new Wire(await endpoint.ConnectAsync(ConnectWithin, ct).ConfigureAwait(false));
            await wire.SendAsync(
                MessageType.Hello,
                new Hello { Version = Wire.OldestVersion, Highest = Wire.Version, Build = FleetVersion.Current, Role = ClientRoles.Control },
                WireJsonContext.Default.Hello,
                ct).ConfigureAwait(false);

            if (await wire.ReceiveAsync(ct).ConfigureAwait(false) is not { Type: MessageType.Welcome })
            {
                return ClipboardImage.Refusal("fleetd did not welcome the clipboard shim");
            }

            await wire.SendAsync(
                MessageType.Request,
                new ControlRequest { Id = 1, Op = op, Pane = caller, Token = token },
                WireJsonContext.Default.ControlRequest,
                ct).ConfigureAwait(false);

            using var within = CancellationTokenSource.CreateLinkedTokenSource(ct);
            within.CancelAfter(AnswerWithin);
            while (await wire.ReceiveAsync(within.Token).ConfigureAwait(false) is { } message)
            {
                if (message.Type != MessageType.Response)
                {
                    continue;
                }

                var response = Wire.Read(message.Payload, WireJsonContext.Default.ControlResponse);
                return response switch
                {
                    { Ok: false } => ClipboardImage.Refusal(response.Error ?? "fleetd refused"),
                    { Data: { Length: > 0 } png } => new ClipboardImage(png),
                    { HasImage: true } => ClipboardImage.Available,
                    _ => ClipboardImage.None,
                };
            }

            return ClipboardImage.Refusal("fleetd closed the connection");
        }
        catch (Exception e) when (e is IOException or TimeoutException or OperationCanceledException or InvalidDataException
                                      or System.Net.Sockets.SocketException or UnauthorizedAccessException
                                      or System.Text.Json.JsonException)
        {
            return ClipboardImage.Refusal($"cannot reach fleetd at {endpoint.Address}: {e.Message}");
        }
    }
}
