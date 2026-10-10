using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipelines;
using Fleet.Platform.Mux.Embedded.Client;
using Fleet.Platform.Mux.Embedded.Daemon;
using Fleet.Platform.Mux.Embedded.Protocol;

namespace Fleet.Tests.Platform.Mux.Embedded;

// A RemoteLink talking to a scripted remote fleetd across a SimulatedLinkStream: the remote
// notes when each message reaches it, so a test can tell how many round trips a refresh costs
// and where a key lands among the refresh's requests.
public sealed class RemoteLinkRefreshTests : IAsyncDisposable
{
    private static readonly TimeSpan OneWay = TimeSpan.FromMilliseconds(150);

    private static readonly string[] RefreshOps = ["list-workspaces", "list-projects", RemoteLink.ProjectConfigsOp, "list-notices"];

    private readonly CancellationTokenSource _stop = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly ConcurrentQueue<(string What, TimeSpan At)> _arrived = new();
    private readonly ConcurrentQueue<TimeSpan> _refreshed = new();
    private readonly HoldableWrites _near;
    private readonly RemoteLink _link;
    private readonly Task _remote;
    private readonly Task _running;

    public RemoteLinkRefreshTests()
    {
        var toRemote = new Pipe();
        var toHome = new Pipe();
        var remoteEnd = new DuplexStream(toRemote.Reader.AsStream(), toHome.Writer.AsStream());
        _near = new HoldableWrites(new SimulatedLinkStream(new DuplexStream(toHome.Reader.AsStream(), toRemote.Writer.AsStream()), OneWay));
        _remote = Task.Run(() => ServeAsync(new Wire(remoteEnd)));
        _link = new RemoteLink("red@far", _ => new RemoteChannel(_near, null, _near), _ => { });
        _link.Noticed += _ => _refreshed.Enqueue(_clock.Elapsed);
        _running = _link.RunAsync(_stop.Token);
    }

    public async ValueTask DisposeAsync()
    {
        _link.Stop();
        await _stop.CancelAsync();
        await _running;
        await _remote.WaitAsync(TimeSpan.FromSeconds(5)).ContinueWith(_ => { });
    }

    private async Task ServeAsync(Wire wire)
    {
        try
        {
            await wire.ReceiveAsync(_stop.Token);
            await wire.SendAsync(MessageType.Welcome, new Welcome { Version = Wire.Version, Build = "test" }, WireJsonContext.Default.Welcome, _stop.Token);

            while (await wire.ReceiveAsync(_stop.Token) is { } message)
            {
                if (message.Type != MessageType.Request)
                {
                    _arrived.Enqueue((message.Type.ToString(), _clock.Elapsed));
                    continue;
                }

                var request = Wire.Read(message.Payload, WireJsonContext.Default.ControlRequest);
                _arrived.Enqueue((request.Op, _clock.Elapsed));
                var response = new ControlResponse { Id = request.Id, Ok = true };
                switch (request.Op)
                {
                    case "status":
                        response.Status = new DaemonStatusDto { Host = "far" };
                        break;
                    case "list-workspaces":
                        response.Workspaces = [new WorkspaceDto { Name = "homelab" }];
                        break;
                    case "list-projects":
                        response.Projects = ["homelab", "dormant"];
                        break;
                    case RemoteLink.ProjectConfigsOp:
                        response.ProjectConfigs = [];
                        break;
                    case "list-notices":
                        response.Notices = [];
                        break;
                }

                await wire.SendAsync(MessageType.Response, response, WireJsonContext.Default.ControlResponse, _stop.Token);
            }
        }
        catch (Exception e) when (e is OperationCanceledException or IOException or ObjectDisposedException)
        {
        }
    }

    private static async Task Eventually(Func<bool> condition, int seconds = 10)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < deadline && !condition())
        {
            await Task.Delay(10);
        }

        Assert.True(condition());
    }

    [Fact]
    public async Task A_refresh_sends_its_four_requests_together_and_waits_one_round_trip_for_them()
    {
        await Eventually(() => !_refreshed.IsEmpty);

        var arrived = _arrived.ToArray();
        var refresh = arrived.SkipWhile(a => a.What != "list-workspaces").Take(4).ToArray();
        Assert.Equal(RefreshOps.Order(), refresh.Select(a => a.What).Order());
        var spread = refresh.Max(a => a.At) - refresh.Min(a => a.At);
        Assert.True(spread < OneWay, $"the requests reached the remote {spread.TotalMilliseconds:0} ms apart");

        var waited = _refreshed.First() - refresh.Min(a => a.At);
        Assert.True(waited < 2 * OneWay, $"the refresh finished {waited.TotalMilliseconds:0} ms after its first request reached the remote");
        Assert.Equal(RemoteLink.Connected, _link.Snapshot().State);
        Assert.Equal(["dormant", "homelab"], _link.Snapshot().Projects);
    }

    [Fact]
    public async Task A_key_sent_during_a_refresh_waits_behind_at_most_the_one_request_being_written()
    {
        await Eventually(() => !_refreshed.IsEmpty);
        var before = _arrived.Count;

        _near.HoldNextWrite();
        await _near.Holding.WaitAsync(TimeSpan.FromSeconds(10));
        var key = _link.ForwardAsync(MessageType.Key, "k"u8.ToArray());
        _near.Release();
        await key;

        await Eventually(() => _arrived.Count >= before + 5);
        var order = _arrived.Skip(before).Take(5).Select(a => a.What).ToArray();
        Assert.Equal("Key", order[1]);
        Assert.Equal(RefreshOps.Order(), order.Where(w => w != "Key").Order());
    }

    private sealed class HoldableWrites(Stream inner) : Stream
    {
        private volatile TaskCompletionSource? _hold;
        private TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource _holding = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Holding => _holding.Task;

        public void HoldNextWrite()
        {
            _holding = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _hold = _released;
        }

        public void Release() => _released.TrySetResult();

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _hold, null) is { } hold)
            {
                _holding.TrySetResult();
                await hold.Task;
            }

            await inner.WriteAsync(buffer, cancellationToken);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            inner.ReadAsync(buffer, cancellationToken);

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => inner.Flush();

        public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
