using System.Net;
using System.Net.Sockets;
using System.Text;
using Fleet.Platform.Releases;

namespace Fleet.Tests.Platform.Releases;

public class HttpReleaseClientTests
{
    [Fact]
    public async Task A_download_slower_than_a_lookup_is_allowed_to_finish()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var body = new byte[256 * 1024];
        Random.Shared.NextBytes(body);

        var server = Task.Run(async () =>
        {
            using var socket = await listener.AcceptTcpClientAsync();
            var stream = socket.GetStream();
            var request = new byte[4096];
            _ = await stream.ReadAsync(request);

            // Longer than the lookup limit the client was given.
            await Task.Delay(TimeSpan.FromMilliseconds(400));
            var header = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(header);
            await stream.WriteAsync(body);
        });

        using var client = new HttpReleaseClient(
            lookupWithin: TimeSpan.FromMilliseconds(200), downloadWithin: TimeSpan.FromSeconds(30));
        var downloaded = await client.DownloadAsync($"http://127.0.0.1:{port}/fleet-win-x64.exe");
        await server;

        Assert.Equal(body, downloaded);
    }

    // HttpClient.Timeout once capped every request at 5 s, so a large download failed; the limits are per call now.
    [Fact]
    public void The_default_client_looks_up_within_15_s_downloads_within_10_minutes_and_sets_no_request_timeout()
    {
        using var client = new HttpReleaseClient();

        Assert.Equal(TimeSpan.FromSeconds(15), client.LookupLimit);
        Assert.Equal(TimeSpan.FromMinutes(10), client.DownloadLimit);
        Assert.Equal(Timeout.InfiniteTimeSpan, client.RequestTimeout);
    }
}