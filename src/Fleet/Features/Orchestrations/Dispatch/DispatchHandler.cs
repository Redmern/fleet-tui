using Fleet.Features.Orchestrations.Dispatch.Models;
using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;
using Fleet.Ports.Harness;
using Fleet.Ports.Mux;
using Fleet.Ports.Mux.Enums;
using Fleet.Ports.Mux.Models;
using Fleet.Shared;
using Fleet.Shared.Constants;
using Fleet.Shared.Orchestrations;
using Fleet.Shared.Orchestrations.Models;
using Fleet.Shared.Results;

namespace Fleet.Features.Orchestrations.Dispatch;

public sealed class DispatchHandler(
    IMuxDriver mux,
    IAgentStore store,
    IHarnessConfig harness,
    TimeSpan? readyTimeout = null,
    TimeSpan? pollInterval = null)
{
    private readonly TimeSpan _readyTimeout = readyTimeout ?? TimeSpan.FromSeconds(30);

    private readonly TimeSpan _pollInterval = pollInterval ?? TimeSpan.FromMilliseconds(200);

    public async Task<Result<DispatchReply>> HandleAsync(
        DispatchCommand command, string stampUtc, CancellationToken ct = default)
    {
        var prompt = command.Prompt.Trim();

        if (prompt.Length == 0)
        {
            return Result<DispatchReply>.Fail(DispatchNote.Nothing);
        }

        var existing = store.List(command.ProjectName)
            .Where(a => AgentHarness.IsOrchestrator(a.Harness))
            .Select(a => a.Branch)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var slug = OrchestrationSlug.Unique(
            OrchestrationSlug.Of(prompt),
            s => existing.Contains(s) || Directory.Exists(OrchestrationPaths.For(command.ProjectRoot, s)));

        var folder = OrchestrationPaths.For(command.ProjectRoot, slug);

        Directory.CreateDirectory(folder);
        Directory.CreateDirectory(OrchestrationPaths.ReportsFolder(folder));

        var brief = new OrchestrationBrief(command.ProjectName, slug, prompt, stampUtc);

        File.WriteAllText(OrchestrationPaths.InstructionsFile(folder), OrchestrationText.Instructions(brief));
        File.WriteAllText(OrchestrationPaths.TaskFile(folder), OrchestrationText.Task(brief));

        harness.WriteForOrchestration(folder, command.ProjectName, slug);

        var record = new AgentRecord(
            folder,
            string.Empty,
            slug,
            AgentHarness.Orchestrator,
            string.Empty,
            RepositoryWasBare: false,
            Hidden: true,
            Open: true,
            Owner: command.Caller,
            Status: OrchestrationStatus.Working);

        store.Save(command.ProjectName, record);

        var pane = await mux.SpawnAsync(
            new SpawnOptions
            {
                Cwd = folder,
                SessionName = FleetWorkspaces.Hidden,
                Workspace = FleetWorkspaces.Hidden,
                NewWindow = true,
                Args = AgentHarness.CommandFor(AgentHarness.Orchestrator, fresh: true),
            },
            ct).ConfigureAwait(false);

        if (pane.IsNone)
        {
            return Result<DispatchReply>.Fail(
                $"the {mux.Name} multiplexer did not respond. Run 'fleet doctor'.");
        }

        await mux.SetTitleAsync(pane, slug, ct).ConfigureAwait(false);

        var browse = await mux.SplitAsync(
            new SplitOptions(pane, SplitDirection.Right)
            {
                Percent = 50,
                Cwd = folder,
                Args = AgentHarness.BrowseCommand,
            },
            ct).ConfigureAwait(false);

        if (!browse.IsNone)
        {
            await mux.SetTitleAsync(browse, $"{slug} files", ct).ConfigureAwait(false);
        }

        await KickOff(folder, pane, ct).ConfigureAwait(false);

        return Result<DispatchReply>.Ok(new DispatchReply(slug, folder, DispatchNote.Dispatched(slug)));
    }

    private async Task KickOff(string folder, PaneId pane, CancellationToken ct)
    {
        var marker = OrchestrationPaths.ReadyMarker(folder);

        var attempts = _pollInterval > TimeSpan.Zero
            ? (int)Math.Ceiling(_readyTimeout / _pollInterval)
            : 0;

        for (var i = 0; i < attempts && !File.Exists(marker); i++)
        {
            await Task.Delay(_pollInterval, ct).ConfigureAwait(false);
        }

        await mux.SendTextAsync(pane, AgentHarness.OrchestratorKickoff + "\r", ct).ConfigureAwait(false);
    }
}
