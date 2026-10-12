using Fleet.Ports.Relay.Models;

namespace Fleet.Ports.Relay;

public interface IRelayQueueStore
{
    IReadOnlyList<string> Projects();

    IReadOnlyList<QueuedPrompt> Load(string project);

    void Save(string project, IReadOnlyList<QueuedPrompt> prompts);
}
