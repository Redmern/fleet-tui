using Fleet.Platform.Mux.Embedded.Daemon;
using Fleet.Platform.Mux.Embedded.Protocol;
using Fleet.Ports;
using Fleet.Shared.Constants;

namespace Fleet.Platform.Mux.Embedded.Client;

public sealed class PaneClipboardImageSource(Endpoint endpoint, string? pane, string? token) : IClipboardImageSource, IClipboardTextSink
{
    public static readonly TimeSpan ConnectWithin = TimeSpan.FromSeconds(3);

    public static readonly TimeSpan AnswerWithin = FleetDaemon.ClipboardImageWithin + TimeSpan.FromSeconds(2);

    public Task<ClipboardImage> ReadPngAsync(CancellationToken ct = default) => AskAsync(FleetDaemon.ClipboardImageOp, ct);

    public Task<ClipboardImage> ProbePngAsync(CancellationToken ct = default) => AskAsync(FleetDaemon.ClipboardTypesOp, ct);

    public async Task<ClipboardCopy> CopyAsync(string text, CancellationToken ct = default)
    {
        var (response, refused) = await RequestAsync(FleetDaemon.ClipboardCopyOp, text, ct).ConfigureAwait(false);
        return refused is not null ? ClipboardCopy.Refusal(refused)
            : response is { Ok: false } ? ClipboardCopy.Refusal(response.Error ?? "fleetd refused")
            : ClipboardCopy.Done;
    }

    private async Task<ClipboardImage> AskAsync(string op, CancellationToken ct)
    {
        var (response, refused) = await RequestAsync(op, null, ct).ConfigureAwait(false);
        return refused is not null ? ClipboardImage.Refusal(refused)
            : response switch
            {
                { Ok: false } => ClipboardImage.Refusal(response.Error ?? "fleetd refused"),
                { Data: { Length: > 0 } png } => new ClipboardImage(png),
                { HasImage: true } => ClipboardImage.Available,
                _ => ClipboardImage.None,
            };
    }

    private async Task<(ControlResponse? Response, string? Refused)> RequestAsync(string op, string? text, CancellationToken ct)
    {
        if (pane is not { Length: > 0 } caller)
        {
            return (null, $"not in a fleet pane ({FleetDaemon.PaneVariable} is not set)");
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
                return (null, "fleetd did not welcome the clipboard shim");
            }

            await wire.SendAsync(
                MessageType.Request,
                new ControlRequest { Id = 1, Op = op, Pane = caller, Token = token, Text = text },
                WireJsonContext.Default.ControlRequest,
                ct).ConfigureAwait(false);

            using var within = CancellationTokenSource.CreateLinkedTokenSource(ct);
            within.CancelAfter(AnswerWithin);
            while (await wire.ReceiveAsync(within.Token).ConfigureAwait(false) is { } message)
            {
                if (message.Type == MessageType.Response)
                {
                    return (Wire.Read(message.Payload, WireJsonContext.Default.ControlResponse), null);
                }
            }

            return (null, "fleetd closed the connection");
        }
        catch (Exception e) when (e is IOException or TimeoutException or OperationCanceledException or InvalidDataException
                                      or System.Net.Sockets.SocketException or UnauthorizedAccessException
                                      or System.Text.Json.JsonException)
        {
            return (null, $"cannot reach fleetd at {endpoint.Address}: {e.Message}");
        }
    }
}
