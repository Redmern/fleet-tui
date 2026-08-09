using Fleet.Platform.Mux;
using Fleet.Platform.Mux.Fake;
using Fleet.Ports.Mux;
using Fleet.Ports.Mux.Enums;
using Fleet.Ports.Mux.Exceptions;
using Fleet.Ports.Mux.Models;

namespace Fleet.Tests.Platform.Mux;

public class FailSilentDriverTests
{
    [Fact]
    public async Task Reads_degrade_to_empty_when_the_mux_is_unavailable()
    {
        var swallowed = new List<Exception>();
        var mux = new FailSilentDriver(new FakeMuxDriver { Available = false }, swallowed.Add);

        Assert.Empty(await mux.ListPanesAsync());
        Assert.Equal(PaneId.None, await mux.SpawnAsync(new SpawnOptions()));
        Assert.Equal(PaneId.None, await mux.SplitAsync(
            new SplitOptions(new PaneId("p1"), SplitDirection.Right)));

        await mux.SetTitleAsync(new PaneId("p1"), "x");
        await mux.FocusPaneAsync(new PaneId("p1"));

        Assert.Equal(5, swallowed.Count);

        Assert.False(await mux.IsAvailableAsync());
        Assert.Equal(5, swallowed.Count);
    }

    [Fact]
    public async Task Successful_calls_pass_straight_through()
    {
        var mux = new FailSilentDriver(new FakeMuxDriver(), _ => { });

        var id = await mux.SpawnAsync(new SpawnOptions { NewWindow = true });

        Assert.False(id.IsNone);
        Assert.Single(await mux.ListPanesAsync());
    }

    [Fact]
    public async Task Programmer_errors_are_not_swallowed()
    {
        var mux = new FailSilentDriver(new ThrowingDriver(new ArgumentNullException("x")), _ => { });

        await Assert.ThrowsAsync<ArgumentNullException>(() => mux.ListPanesAsync());
    }

    [Fact]
    public async Task An_unexpected_exception_type_is_not_swallowed()
    {
        var mux = new FailSilentDriver(new ThrowingDriver(new NotSupportedException()), _ => { });

        await Assert.ThrowsAsync<NotSupportedException>(() => mux.SpawnAsync(new SpawnOptions()));
    }

    [Fact]
    public void Identity_is_delegated_to_the_inner_driver()
    {
        var inner = new FakeMuxDriver { CurrentPane = new PaneId("p7") };
        var mux = new FailSilentDriver(inner, _ => { });

        Assert.Equal("fake", mux.Name);
        Assert.Equal(inner.Caps, mux.Caps);
        Assert.Equal(new PaneId("p7"), mux.CurrentPane);
    }

    private sealed class ThrowingDriver(Exception toThrow) : IMuxDriver
    {
        public string Name => "throwing";

        public MuxCaps Caps => MuxCaps.None;

        public PaneId CurrentPane => PaneId.None;

        public Task<bool> IsAvailableAsync(CancellationToken ct = default) => throw toThrow;

        public Task<IReadOnlyList<Pane>> ListPanesAsync(CancellationToken ct = default) => throw toThrow;

        public Task<PaneId> SpawnAsync(SpawnOptions o, CancellationToken ct = default) => throw toThrow;

        public Task<PaneId> SplitAsync(SplitOptions o, CancellationToken ct = default) => throw toThrow;

        public Task MovePaneAsync(
            PaneId id, MovePaneOptions options, CancellationToken ct = default) => throw toThrow;

        public Task SetTitleAsync(PaneId id, string t, CancellationToken ct = default) => throw toThrow;

        public Task FocusPaneAsync(PaneId id, CancellationToken ct = default) => throw toThrow;
    }
}