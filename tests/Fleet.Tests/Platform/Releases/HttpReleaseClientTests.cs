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

            // Longer than the old whole-request limit of 5 seconds.
            await Task.Delay(TimeSpan.FromSeconds(6));
            var header = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(header);
            await stream.WriteAsync(body);
        });

        using var client = new HttpReleaseClient();
        var downloaded = await client.DownloadAsync($"http://127.0.0.1:{port}/fleet-win-x64.exe");
        await server;

        Assert.Equal(body, downloaded);
        Assert.True(HttpReleaseClient.DownloadWithin >= TimeSpan.FromMinutes(5));
    }
}