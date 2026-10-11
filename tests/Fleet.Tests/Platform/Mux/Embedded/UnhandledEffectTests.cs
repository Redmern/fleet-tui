using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Fleet.Platform.Forwards;
using Fleet.Platform.Mux.Embedded;
using Fleet.Platform.Mux.Embedded.Daemon;
using Fleet.Platform.Mux.Embedded.Protocol;
using Fleet.Shared.Constants;
using Fleet.Tests.Platform.Forwards;

namespace Fleet.Tests.Platform.Mux.Embedded;

public sealed class UnhandledEffectTests : IAsyncLifetime
{
    private readonly CancellationTokenSource _stop = new();
    private readonly TcpListener _far = new(IPAddress.Loopback, 0);
    private readonly ConcurrentQueue<string> _log = new();
    private readonly TaskCompletionSource<ControlRequest> _answered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<Task> _running = [];
    private Endpoint _home = null!;

    public Task InitializeAsync()
    {
        _far.Start();
        var name = $"fleet-test-{Guid.NewGuid():N}"[..20];
        _home = new Endpoint(OperatingSystem.IsWindows() ? name : Path.Combine(Path.GetTempPath(), name + ".sock"));
        var panes = new FakePanes();
        var home = new FleetDaemon(new DaemonOptions
        {
            Endpoint = _home,
            Pty = panes.NewPty,
            Terminal = panes.NewTerminal,
            Log = _log.Enqueue,
            RemoteOpen = (_, _) =>
            {
                var client = new TcpClient();
                client.Connect((IPEndPoint)_far.LocalEndpoint);
                var stream = client.GetStream();
                return new RemoteChannel(stream, null, client);
            },
            Forwards = new ForwardOptions
            {
                ControlPath = host => "/tmp/cm-" + host,
                Ssh = new FakeSsh().RunAsync,
                Healthy = (_, _) => Task.FromResult(true),
                ReconnectAfter = TimeSpan.FromMilliseconds(100),
            },
        });

        _running.Add(home.RunAsync(_stop.Token));
        _running.Add(Task.Run(() => ServeFarAsync(_stop.Token)));
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _stop.CancelAsync();
        _far.Stop();
        await Task.WhenAll(_running.Select(t => t.ContinueWith(_ => { }, TaskScheduler.Default)));
    }

    private async Task ServeFarAsync(CancellationToken ct)
    {
        using var accepted = await _far.AcceptTcpClientAsync(ct);
        using var wire = new Wire(accepted.GetStream());
        await wire.ReceiveAsync(ct);
        await wire.SendAsync(
            MessageType.Welcome,
            new Welcome { Version = Wire.Version, Client = "c1", Build = FleetVersion.Current },
            WireJsonContext.Default.Welcome,
            ct);

        var sentEffect = false;
        while (await wire.ReceiveAsync(ct) is { } message)
        {
            if (message.Type != MessageType.Request)
            {
                continue;
            }

            var request = Wire.Read(message.Payload, WireJsonContext.Default.ControlRequest);
            if (request.Op == ForwardHub.ViewerForwardedOp)
            {
                _answered.TrySetResult(request);
            }

            await wire.SendAsync(
                MessageType.Response, new ControlResponse { Id = request.Id, Ok = true }, WireJsonContext.Default.ControlResponse, ct);

            if (!sentEffect)
            {
                sentEffect = true;
                await wire.SendAsync(
                    MessageType.HostEffect,
                    new HostEffect { Kind = "teleport-port", Value = "5173", Reply = "r1" },
                    WireJsonContext.Default.HostEffect,
                    ct);
            }
        }
    }

    [Fact]
    public async Task An_effect_the_viewer_does_not_handle_is_logged_and_answered_as_unsupported()
    {
        using var home = new EmbeddedDriver(_home);
        await home.ConnectRemoteAsync("red@far");

        var answer = await _answered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal("r1", answer.Reply);
        Assert.Equal(ForwardResults.Unsupported, answer.ForwardResult?.State);
        Assert.Contains("teleport-port", answer.ForwardResult?.Reason, StringComparison.Ordinal);
        Assert.Contains(_log, l => l.Contains("ignored", StringComparison.Ordinal) && l.Contains("teleport-port", StringComparison.Ordinal));
    }
}
