using Fleet.Platform.Storage;
using Fleet.Ports;

namespace Fleet.Tests.Platform.Storage;

public sealed class FileNotifyStoreTests : ConfigHomeFixture
{
    [Fact]
    public void Draining_returns_every_queued_line_once_and_empties_the_queue()
    {
        var store = new FileNotifyStore();
        store.Notify("approval needed in techweb: new_agent by upgrade");
        store.Notify("backend/login reported done");

        Assert.Equal(
            ["approval needed in techweb: new_agent by upgrade", "backend/login reported done"],
            FileNotifyStore.Drain());
        Assert.Empty(FileNotifyStore.Drain());
    }

    [Fact]
    public void Draining_an_empty_queue_returns_nothing()
    {
        Assert.Empty(FileNotifyStore.Drain());
    }

    [Fact]
    public void A_line_queued_after_a_drain_is_kept_for_the_next_one()
    {
        var store = new FileNotifyStore();
        store.Notify("first");
        FileNotifyStore.Drain();

        store.Notify("second");

        Assert.Equal(["second"], FileNotifyStore.Drain());
    }

    [Fact]
    public async Task Lines_written_while_the_queue_is_drained_are_neither_lost_nor_repeated()
    {
        const int Written = 3000;
        var store = new FileNotifyStore();
        var drained = new List<string>();
        var writing = true;

        var writer = Task.Run(() =>
        {
            for (var i = 0; i < Written; i++)
            {
                store.Notify($"line {i}");
            }

            Volatile.Write(ref writing, false);
        });

        while (Volatile.Read(ref writing))
        {
            drained.AddRange(FileNotifyStore.Drain());
            Thread.Yield();
        }

        await writer.WaitAsync(TimeSpan.FromSeconds(60));
        drained.AddRange(FileNotifyStore.Drain());

        Assert.Equal(Enumerable.Range(0, Written).Select(i => $"line {i}"), drained);
    }

    [Fact]
    public void A_drain_that_finds_the_queue_locked_returns_at_once_and_loses_nothing()
    {
        new FileNotifyStore().Notify("kept");

        using (new FileStream(FileNotifyStore.File, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.Empty(FileNotifyStore.Drain());
        }

        Assert.Equal(["kept"], FileNotifyStore.Drain());
    }

    [Fact]
    public void A_notify_that_gives_up_on_a_locked_queue_says_so_in_the_log()
    {
        var log = new Log();
        new FileNotifyStore().Notify("first");

        using (new FileStream(FileNotifyStore.File, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            new FileNotifyStore(log, attempts: 2).Notify("dropped line");
        }

        Assert.Contains("dropped line", Assert.Single(log.Lines));
        Assert.Equal(["first"], FileNotifyStore.Drain());
    }

    private sealed class Log : IFleetLog
    {
        public List<string> Lines { get; } = [];

        public void Swallowed(Exception e)
        {
        }

        public void Write(string line) => Lines.Add(line);

        public IReadOnlyList<string> Tail(int lines) => Lines;
    }
}
