using Fleet.Ports;
using Fleet.Ports.Orchestrations;
using Fleet.Shared.Orchestrations;
using Fleet.Shared.Settings.Models;

namespace Fleet.Tests.Ports;

public class ModelRouterExtensionsTests
{
    private static readonly ModelRoutingRequest Request = new("fix it", null, false, null);

    private readonly RecordingLog _log = new();

    [Fact]
    public async Task A_router_answer_is_passed_through()
    {
        IModelRouter router = new FakeRouter(() => Task.FromResult<RoleModel?>(new RoleModel("opus", "high")));

        Assert.Equal(new RoleModel("opus", "high"), await router.TryChooseAsync(Request, _log));
        Assert.Empty(_log.Lines);
    }

    [Fact]
    public async Task No_router_yields_nothing()
    {
        IModelRouter? router = null;

        Assert.Null(await router.TryChooseAsync(Request, _log));
    }

    [Fact]
    public async Task A_router_that_throws_yields_nothing_and_the_exception_is_logged()
    {
        IModelRouter router = new FakeRouter(() => throw new InvalidOperationException("claude is unreachable"));

        Assert.Null(await router.TryChooseAsync(Request, _log));
        Assert.Contains("claude is unreachable", Assert.Single(_log.Lines));
    }

    [Fact]
    public async Task A_router_timing_out_on_its_own_yields_nothing()
    {
        IModelRouter router = new FakeRouter(() => throw new OperationCanceledException());

        Assert.Null(await router.TryChooseAsync(Request, _log));
    }

    [Fact]
    public async Task Cancelling_the_caller_still_cancels()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        IModelRouter router = new FakeRouter(() => throw new OperationCanceledException(cts.Token));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => router.TryChooseAsync(Request, _log, cts.Token));
    }

    private sealed class FakeRouter(Func<Task<RoleModel?>> answer) : IModelRouter
    {
        public Task<RoleModel?> ChooseAsync(ModelRoutingRequest request, CancellationToken ct = default) => answer();
    }

    private sealed class RecordingLog : IFleetLog
    {
        public List<string> Lines { get; } = [];

        public void Swallowed(Exception e) => Lines.Add(e.Message);

        public void Write(string line) => Lines.Add(line);

        public IReadOnlyList<string> Tail(int lines) => Lines.TakeLast(lines).ToList();
    }
}
