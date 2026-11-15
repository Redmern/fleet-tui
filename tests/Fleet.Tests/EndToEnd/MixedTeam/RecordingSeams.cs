using Fleet.Ports.Harness;
using Fleet.Ports.Orchestrations;
using Fleet.Shared.Results;

namespace Fleet.Tests.EndToEnd.MixedTeam;

// What a harness config writer was asked to write: for which harness, for an agent or an orchestration, and where.
internal sealed record ConfigWrite(string Harness, string Kind, string Folder, string Project, string Who);

// Records every write, then passes it on to the wrapped writers (which a test points at temp files, or at nothing).
internal sealed class RecordingHarnessConfigs(IHarnessConfigs inner) : IHarnessConfigs
{
    private readonly List<ConfigWrite> _writes = [];

    public IReadOnlyList<ConfigWrite> Writes
    {
        get
        {
            lock (_writes)
            {
                return [.. _writes];
            }
        }
    }

    public IHarnessConfig For(string harness) => new Recording(this, harness, inner.For(harness));

    private void Add(ConfigWrite write)
    {
        lock (_writes)
        {
            _writes.Add(write);
        }
    }

    private sealed class Recording(RecordingHarnessConfigs owner, string harness, IHarnessConfig inner) : IHarnessConfig
    {
        public Result WriteForOrchestration(string folder, string project, string caller)
        {
            owner.Add(new ConfigWrite(harness, "orchestration", folder, project, caller));

            return inner.WriteForOrchestration(folder, project, caller);
        }

        public Result WriteForAgent(string folder, string project, string repository, string branch)
        {
            owner.Add(new ConfigWrite(harness, "agent", folder, project, $"{repository}/{branch}"));

            return inner.WriteForAgent(folder, project, repository, branch);
        }
    }
}

internal sealed class RecordingSlugNamer : ISlugNamer
{
    private readonly List<string> _prompts = [];

    // What the namer answers; null is "no name", and fleet then makes the slug from the prompt.
    public string? Answer { get; set; }

    public IReadOnlyList<string> Prompts
    {
        get
        {
            lock (_prompts)
            {
                return [.. _prompts];
            }
        }
    }

    public Task<string?> NameAsync(string prompt, CancellationToken ct = default)
    {
        lock (_prompts)
        {
            _prompts.Add(prompt);
        }

        return Task.FromResult(Answer);
    }
}
