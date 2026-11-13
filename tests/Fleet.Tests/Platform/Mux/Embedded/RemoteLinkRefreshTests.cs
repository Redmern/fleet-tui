using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipelines;
using Fleet.Platform.Mux.Embedded.Client;
using Fleet.Platform.Mux.Embedded.Daemon;
using Fleet.Platform.Mux.Embedded.Protocol;
using Fleet.Tests.Support;

namespace Fleet.Tests.Platform.Mux.Embedded;

// A RemoteLink talking to a scripted remote fleetd across a SimulatedLinkStream: the remote
// notes when each message reaches it, so a test can tell how many round trips a refresh costs
// and where a key lands among the refresh's requests.
// These bound real latency on a simulated link, so each check builds its own rig and retries once.
public sealed class RemoteLinkRefreshTests
{
    // The bounds below sit halfway between one round trip and the next: four requests sent
    // one after another would reach the remote two one-way hops apart, and a refresh that
    // took a second round trip would finish three hops after its first request, not one.
    // So the runner gets a whole one-way hop of slack; 300 ms of it covers a Windows runner's
    // coarse timers and a busy thread pool, where 150 ms did not.
    private static readonly TimeSpan OneWay = TimeSpan.FromMilliseconds(300);

    private static readonly string[] RefreshOps = ["list-workspaces", "list-projects", RemoteLink.ProjectConfigsOp, "list-notices"];

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
    public Task A_refresh_sends_its_four_requests_together_and_waits_one_round_trip_for_them() =>
        Timing.RetryOnceAsync(async () =>
        {
            await using var rig = new Rig();
            await Eventually(() => !rig.Refreshed.IsEmpty);
            var slowFrom = rig.Slow();
            await Eventually(() => rig.Refreshed.Count >= 2);

            var arrived = rig.Arrived.Where(a => a.At >= slowFrom).ToArray();
            var refresh = arrived.SkipWhile(a => a.What != "list-workspaces").Take(4).ToArray();
            Assert.Equal(RefreshOps.Order(), refresh.Select(a => a.What).Order());
            var spread = refresh.Max(a => a.At) - refresh.Min(a => a.At);
            Assert.True(spread < OneWay, $"the requests reached the remote {spread.TotalMilliseconds:0} ms apart");

            var asked = refresh.Min(a => a.At);
            await Eventually(() => rig.Refreshed.Any(t => t > asked));
            var waited = rig.Refreshed.First(t => t > asked) - asked;
            Assert.True(waited < 2 * OneWay, $"the refresh finished {waited.TotalMilliseconds:0} ms after its first request reached the remote");
            Assert.Equal(RemoteLink.Connected, rig.Link.Snapshot().State);
            Assert.Equal(["dormant", "homelab"], rig.Link.Snapshot().Projects);
        });

    [Fact]
    public Task A_key_sent_during_a_refresh_waits_behind_at_most_the_one_request_being_written() =>
        Timing.RetryOnceAsync(async () =>
        {
            await using var rig = new Rig();
            await Eventually(() => !rig.Refreshed.IsEmpty);
            rig.Slow();
            var before = rig.Arrived.Count;

            rig.Near.HoldNextWrite();
            await rig.Near.Holding.WaitAsync(TimeSpan.FromSeconds(10));
            var key = rig.Link.ForwardAsync(MessageType.Key, "k"u8.ToArray());
            rig.Near.Release();
            await key;

            await Eventually(() => rig.Arrived.Count >= before + 5);
            var order = rig.Arrived.Skip(before).Take(5).Select(a => a.What).ToArray();
            Assert.Equal("Key", order[1]);
            Assert.Equal(RefreshOps.Order(), order.Where(w => w != "Key").Order());
        });

    // A RemoteLink that refreshes every 200 ms, so the second refresh comes soon after the first. The link connects
    // and makes its first refresh with no delay; Slow() then gives it the one-way delay the checks measure against.
    private sealed class Rig : IAsyncDisposable
    {
        private readonly SimulatedLinkStream _wire;
        private readonly CancellationTokenSource _stop = new();
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly Task _remote;
        private readonly Task _running;

        public Rig()
        {
            var toRemote = new Pipe();
            var toHome = new Pipe();
            var remoteEnd = new DuplexStream(toRemote.Reader.AsStream(), toHome.Writer.AsStream());
            _wire = new SimulatedLinkStream(new DuplexStream(toHome.Reader.AsStream(), toRemote.Writer.AsStream()), TimeSpan.Zero);
            Near = new HoldableWrites(_wire);
            _remote = Task.Run(() => ServeAsync(new Wire(remoteEnd)));
            Link = new RemoteLink("red@far", _ => new RemoteChannel(Near, null, Near), _ => { })
            {
                RefreshPeriod = TimeSpan.FromMilliseconds(200),
            };
            Link.Noticed += _ => Refreshed.Enqueue(_clock.Elapsed);
            _running = Link.RunAsync(_stop.Token);
        }

        public ConcurrentQueue<(string What, TimeSpan At)> Arrived { get; } = new();

        public ConcurrentQueue<TimeSpan> Refreshed { get; } = new();

        public HoldableWrites Near { get; }

        public RemoteLink Link { get; }

        public TimeSpan Slow()
        {
            _wire.OneWay = OneWay;
            return _clock.Elapsed;
        }

        public async ValueTask DisposeAsync()
        {
            Link.Stop();
            await _stop.CancelAsync();
            await _running;
            await _remote.WaitAsync(TimeSpan.FromSeconds(5)).ContinueWith(_ => { });
            _stop.Dispose();
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
                        Arrived.Enqueue((message.Type.ToString(), _clock.Elapsed));
                        continue;
                    }

                    var request = Wire.Read(message.Payload, WireJsonContext.Default.ControlRequest);
                    Arrived.Enqueue((request.Op, _clock.Elapsed));
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
