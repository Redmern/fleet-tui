using Fleet.Features.Orchestrations.Dispatch.Models;
using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;
using Fleet.Ports.Harness;
using Fleet.Ports.Mux;
using Fleet.Ports.Orchestrations;
using Fleet.Ports.Mux.Enums;
using Fleet.Ports.Mux.Models;
using Fleet.Ports.Settings;
using Fleet.Shared;
using Fleet.Shared.Constants;
using Fleet.Shared.Orchestrations;
using Fleet.Shared.Orchestrations.Models;
using Fleet.Shared.Results;
using Fleet.Shared.Settings.Enums;
using Fleet.Shared.Settings.Models;

namespace Fleet.Features.Orchestrations.Dispatch;

public sealed class DispatchHandler(
    IMuxDriver mux,
    IAgentStore store,
    IHarnessConfig harness,
    TimeSpan? readyTimeout = null,
    TimeSpan? pollInterval = null,
    IDispatchHistory? history = null,
    ISlugNamer? namer = null,
    ISettingsStore? settings = null)
{
    private readonly TimeSpan _readyTimeout = readyTimeout ?? TimeSpan.FromSeconds(30);

    private readonly TimeSpan _pollInterval = pollInterval ?? TimeSpan.FromMilliseconds(200);

    public async Task<Result<DispatchReply>> HandleAsync(
        DispatchCommand command, string stampUtc, CancellationToken ct = default)
    {
        var trimmed = command.Prompt.Trim();

        if (trimmed.Length == 0)
        {
            return Result<DispatchReply>.Fail(DispatchNote.Nothing);
        }

        var (prompt, useAidlc) = ResolveAidlc(trimmed, command.ProjectName);

        if (prompt.Length == 0)
        {
            return Result<DispatchReply>.Fail(DispatchNote.Nothing);
        }

        var existing = store.List(command.ProjectName)
            .Where(a => AgentHarness.IsOrchestrator(a.Harness))
            .Select(a => a.Branch)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var named = await NameOrNull(prompt, ct).ConfigureAwait(false);

        var slug = OrchestrationSlug.Unique(
            OrchestrationSlug.Of(string.IsNullOrWhiteSpace(named) ? prompt : named),
            s => existing.Contains(s) || Directory.Exists(OrchestrationPaths.For(command.ProjectRoot, s)));

        var folder = OrchestrationPaths.For(command.ProjectRoot, slug);

        Directory.CreateDirectory(folder);
        Directory.CreateDirectory(OrchestrationPaths.ReportsFolder(folder));

        var brief = new OrchestrationBrief(command.ProjectName, slug, prompt, stampUtc);
        var howYouWork = HowYouWorkOverride(command.ProjectRoot);
        var aidlc = useAidlc ? (AidlcOverride(command.ProjectRoot) ?? OrchestrationText.DefaultAidlc) : null;

        File.WriteAllText(
            OrchestrationPaths.InstructionsFile(folder),
            OrchestrationText.Instructions(brief, howYouWork, aidlc));
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

        var panes = await mux.ListPanesAsync(ct).ConfigureAwait(false);
        var active = panes.FirstOrDefault(p => p.IsActive);
        var window = panes.FirstOrDefault(p => p.Id == mux.CurrentPane)?.WindowId
            ?? panes.FirstOrDefault(p => PathKey.Same(p.Cwd, command.ProjectRoot))?.WindowId;

        var pane = await mux.SpawnAsync(
            new SpawnOptions
            {
                Cwd = folder,
                SessionName = command.ProjectName,
                WindowId = window,
                NewWindow = window is null,
                Args = AgentHarness.CommandFor(AgentHarness.Orchestrator),
            },
            ct).ConfigureAwait(false);

        if (pane.IsNone)
        {
            return Result<DispatchReply>.Fail(
                $"the {mux.Name} multiplexer did not respond. Run 'fleet doctor'.");
        }

        await mux.SetTitleAsync(pane, slug, ct).ConfigureAwait(false);

        await mux.SplitAsync(
            new SplitOptions(pane, SplitDirection.Right)
            {
                Percent = 50,
                Cwd = folder,
                Args = AgentHarness.BrowseCommandFor(AgentPaneMatch.BrowserTitle(record)),
            },
            ct).ConfigureAwait(false);

        history?.Add(command.ProjectName, prompt);

        if (active is not null)
        {
            await mux.FocusPaneAsync(active.Id, ct).ConfigureAwait(false);
        }

        await KickOff(folder, ct).ConfigureAwait(false);

        return Result<DispatchReply>.Ok(new DispatchReply(slug, folder, DispatchNote.Dispatched(slug)));
    }

    private static string? HowYouWorkOverride(string projectRoot)
    {
        var path = ProjectConfigPaths.InstructionsFile(projectRoot);

        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? AidlcOverride(string projectRoot)
    {
        var path = ProjectConfigPaths.AidlcFile(projectRoot);

        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private (string Prompt, bool UseAidlc) ResolveAidlc(string prompt, string projectName)
    {
        var config = settings?.Load(projectName) ?? SettingsConfig.Default;

        if (config.Aidlc == AidlcMode.Off)
        {
            return (prompt, false);
        }

        if (config.Aidlc == AidlcMode.On)
        {
            return (prompt, true);
        }

        var trimmed = prompt.TrimStart();

        if (config.Trigger.Length > 0 && trimmed.StartsWith(config.Trigger, StringComparison.Ordinal))
        {
            return (trimmed[config.Trigger.Length..].TrimStart(), true);
        }

        return (prompt, false);
    }

    private async Task<string?> NameOrNull(string prompt, CancellationToken ct)
    {
        if (namer is null)
        {
            return null;
        }

        try
        {
            return await namer.NameAsync(prompt, ct).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return null;
        }
    }

    private async Task KickOff(string folder, CancellationToken ct)
    {
        var marker = OrchestrationPaths.ReadyMarker(folder);

        var attempts = _pollInterval > TimeSpan.Zero
            ? (int)Math.Ceiling(_readyTimeout / _pollInterval)
            : 0;

        for (var i = 0; i < attempts && !File.Exists(marker); i++)
        {
            await Task.Delay(_pollInterval, ct).ConfigureAwait(false);
        }

        var inbox = Path.Combine(folder, ".fleet");
        Directory.CreateDirectory(inbox);

        await File.WriteAllTextAsync(
                Path.Combine(inbox, AgentHarness.AgentInstructionFile),
                AgentHarness.OrchestratorKickoff,
                ct)
            .ConfigureAwait(false);
    }
}
