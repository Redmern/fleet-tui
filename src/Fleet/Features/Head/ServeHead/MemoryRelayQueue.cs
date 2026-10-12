using Fleet.Ports.Relay;
using Fleet.Ports.Relay.Models;

namespace Fleet.Features.Head.ServeHead;

public sealed class MemoryRelayQueue : IRelayQueueStore
{
    private readonly Lock _lock = new();

    private readonly Dictionary<string, List<QueuedPrompt>> _queues = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> Projects()
    {
        lock (_lock)
        {
            return [.. _queues.Where(q => q.Value.Count > 0).Select(q => q.Key)];
        }
    }

    public IReadOnlyList<QueuedPrompt> Load(string project)
    {
        lock (_lock)
        {
            return _queues.TryGetValue(project, out var queue) ? [.. queue] : [];
        }
    }

    public void Save(string project, IReadOnlyList<QueuedPrompt> prompts)
    {
        lock (_lock)
        {
            _queues[project] = [.. prompts];
        }
    }
}
