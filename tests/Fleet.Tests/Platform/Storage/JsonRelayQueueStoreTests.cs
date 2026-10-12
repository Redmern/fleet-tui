using Fleet.Platform.Storage;
using Fleet.Ports.Relay.Models;

namespace Fleet.Tests.Platform.Storage;

public sealed class JsonRelayQueueStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "fleet-relay-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void A_saved_queue_reads_back_in_order_and_lists_its_project()
    {
        var at = new DateTimeOffset(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);
        var store = new JsonRelayQueueStore(_folder);

        store.Save("Web", [new QueuedPrompt("first", at), new QueuedPrompt("second\nline", at.AddMinutes(1))]);

        var again = new JsonRelayQueueStore(_folder);
        Assert.Equal([new QueuedPrompt("first", at), new QueuedPrompt("second\nline", at.AddMinutes(1))], again.Load("web"));
        Assert.Equal(["Web"], again.Projects());
    }

    [Fact]
    public void Emptying_a_queue_removes_it_even_before_the_folder_exists()
    {
        var store = new JsonRelayQueueStore(_folder);

        store.Save("web", []);
        store.Save("web", [new QueuedPrompt("x", DateTimeOffset.Now)]);
        store.Save("web", []);

        Assert.Empty(store.Load("web"));
        Assert.Empty(store.Projects());
    }

    [Fact]
    public void A_corrupt_queue_file_is_kept_aside_rather_than_overwritten()
    {
        var store = new JsonRelayQueueStore(_folder);
        Directory.CreateDirectory(_folder);
        File.WriteAllText(store.FileFor("web"), "{ not json");

        Assert.Empty(store.Load("web"));
        Assert.Single(Directory.GetFiles(_folder, "*.corrupt"));
    }

    [Fact]
    public void Project_names_that_strip_to_the_same_file_name_keep_separate_queues()
    {
        var at = DateTimeOffset.Now;
        var store = new JsonRelayQueueStore(_folder);

        store.Save("a/b", [new QueuedPrompt("for a/b", at)]);
        store.Save("ab", [new QueuedPrompt("for ab", at)]);

        var again = new JsonRelayQueueStore(_folder);
        Assert.NotEqual(store.FileFor("a/b"), store.FileFor("ab"));
        Assert.Equal("for a/b", Assert.Single(again.Load("a/b")).Text);
        Assert.Equal("for ab", Assert.Single(again.Load("ab")).Text);
        Assert.Equal(["a/b", "ab"], again.Projects().Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_queue_that_cannot_be_written_still_works_in_memory_and_never_throws()
    {
        File.WriteAllText(_folder, "a file where the folder should be");

        try
        {
            var store = new JsonRelayQueueStore(_folder);

            store.Save("web", [new QueuedPrompt("first", DateTimeOffset.Now)]);
            store.Save("web", [.. store.Load("web"), new QueuedPrompt("second", DateTimeOffset.Now)]);

            Assert.Equal(["first", "second"], store.Load("web").Select(q => q.Text));
            Assert.Equal(["web"], store.Projects());

            store.Save("web", []);
            Assert.Empty(store.Load("web"));
        }
        finally
        {
            File.Delete(_folder);
        }
    }
}
