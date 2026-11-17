using Fleet.Ports.Forge;
using Fleet.Ports.Forge.Enums;

namespace Fleet.Platform.Forge;

public sealed class HttpForgeProbe(HttpMessageHandler? handler = null, TimeSpan? timeout = null) : IForgeProbe
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(4);

    private const int MaxBody = 4096;

    private readonly HttpClient _client = new(handler ?? new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = Timeout.InfiniteTimeSpan,
    };

    public static Uri? Address(string host)
    {
        var name = host.Trim();
        var colon = name.LastIndexOf(':');
        var port = colon > 0 && !name.EndsWith(']') ? name[(colon + 1)..] : string.Empty;
        var bare = port.Length > 0 ? name[..colon] : name;

        return bare.Length > 0
            && Uri.CheckHostName(bare.Trim('[', ']')) != UriHostNameType.Unknown
            && (port.Length == 0 || (int.TryParse(port, out var number) && number is > 0 and < 65536))
            && Uri.TryCreate($"https://{name}{ForgeVersionAnswer.Path}", UriKind.Absolute, out var address)
                ? address
                : null;
    }

    public async Task<ForgeProbeResult> ProbeAsync(string host, CancellationToken ct = default)
    {
        if (Address(host) is not { } address)
        {
            return ForgeProbeResult.NotAForge;
        }

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(timeout ?? DefaultTimeout);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, address);
            request.Headers.Accept.ParseAdd("application/json");

            using var response = await _client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, limit.Token)
                .ConfigureAwait(false);

            return ForgeVersionAnswer.Read(
                response.StatusCode, await BodyAsync(response, limit.Token).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return ForgeProbeResult.Unreachable;
        }
        catch (Exception e) when (e is HttpRequestException or IOException)
        {
            return ForgeProbeResult.Unreachable;
        }
    }

    private static async Task<string> BodyAsync(HttpResponseMessage response, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);

        var buffer = new byte[MaxBody];
        var read = 0;

        while (read < buffer.Length)
        {
            var got = await stream.ReadAsync(buffer.AsMemory(read), ct).ConfigureAwait(false);

            if (got == 0)
            {
                break;
            }

            read += got;
        }

        return System.Text.Encoding.UTF8.GetString(buffer, 0, read);
    }
}
