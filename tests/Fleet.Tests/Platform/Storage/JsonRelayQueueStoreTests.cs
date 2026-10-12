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
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "queue-web.json"), "{ not json");
        var store = new JsonRelayQueueStore(_folder);

        Assert.Empty(store.Load("web"));
        Assert.Single(Directory.GetFiles(_folder, "*.corrupt"));
    }
}
