using Fleet.Features.Orchestrations.Dispatch.Models;
using Fleet.Ports.Agents;
using Fleet.Ports.Aidlc;
using Fleet.Ports.Agents.Models;
using Fleet.Ports.Harness;
using Fleet.Ports.Mux;
using Fleet.Ports.Orchestrations;
using Fleet.Ports.Mux.Enums;
using Fleet.Ports.Mux.Models;
using Fleet.Ports.Settings;
using Fleet.Shared;
using Fleet.Shared.Aidlc;
using Fleet.Shared.Aidlc.Enums;
using Fleet.Shared.Aidlc.Models;
using Fleet.Shared.Constants;
using Fleet.Shared.Orchestrations;
using Fleet.Shared.Orchestrations.Models;
using Fleet.Shared.Results;
using Fleet.Shared.Settings;
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
    ISettingsStore? settings = null,
    IIntentStore? intents = null)
{
    private readonly TimeSpan _readyTimeout = readyTimeout ?? TimeSpan.FromSeconds(60);

    private readonly TimeSpan _pollInterval = pollInterval ?? TimeSpan.FromMilliseconds(200);

    public async Task<Result<DispatchReply>> HandleAsync(
        DispatchCommand command, string stampUtc, CancellationToken ct = default)
    {
        var trimmed = command.Prompt.Trim();

        if (trimmed.Length == 0)
        {
            return Result<DispatchReply>.Fail(DispatchNote.Nothing);
        }

        var config = settings?.Load(command.ProjectName) ?? SettingsConfig.Default;

        Profile? argument = null;

        if (!string.IsNullOrWhiteSpace(command.Profile))
        {
            argument = Words.Parse<Profile>(command.Profile);

            if (argument is null)
            {
                return Result<DispatchReply>.Fail(DispatchNote.UnknownProfile(command.Profile));
            }
        }

        var (prompt, aidlc) = ResolveAidlc(trimmed, config.Aidlc.Mode, config.Aidlc.DefaultProfile, argument);

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
        string? process = null;

        if (aidlc is var (profile, source))
        {
            var plan = config.Aidlc.Plan(profile);

            StartIntent(folder, Intake.Start(slug, plan, source, stampUtc));
            process = ProcessText.Render(plan, AidlcOverride(command.ProjectRoot));
        }

        File.WriteAllText(
            OrchestrationPaths.InstructionsFile(folder),
            OrchestrationText.Instructions(brief, howYouWork, process));
        File.WriteAllText(OrchestrationPaths.TaskFile(folder), OrchestrationText.Task(brief));

        harness.WriteForOrchestration(folder, command.ProjectName, slug);

        await LeaveKickoff(folder, ct).ConfigureAwait(false);

        var loaded = settings?.Load(command.ProjectName);
        var inNvim = loaded?.SubOrchestratorsInNvim ?? SettingsDefaults.SubOrchestratorsInNvim;
        var launch = ClaudeLaunch.ForAgent(
            command.ProjectName, string.Empty, slug, orchestrator: true, loaded?.Models ?? SettingsDefaults.Models);

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
            Status: OrchestrationStatus.Working,
            InNvim: inNvim);

        store.Save(command.ProjectName, record);

        var panes = await mux.ListPanesAsync(ct).ConfigureAwait(false);
        var active = panes.FirstOrDefault(p => p.IsActive);

        var pane = await mux.SpawnAsync(
            HiddenSpawn.Into(
                mux,
                command.ProjectName,
                new SpawnOptions
                {
                    Cwd = folder,
                    Args = AgentHarness.CommandFor(AgentHarness.Orchestrator, orchestratorInNvim: inNvim, launch: launch),
                    Env = AgentHarness.SpawnEnv(AgentHarness.Orchestrator, inNvim),
                }),
            ct).ConfigureAwait(false);

        if (pane.IsNone)
        {
            return Result<DispatchReply>.Fail(
                $"the {mux.Name} multiplexer did not respond. Run 'fleet doctor'.");
        }

        await mux.SetTitleAsync(pane, slug, ct).ConfigureAwait(false);

        history?.Add(command.ProjectName, prompt);

        if (active is not null)
        {
            await mux.FocusPaneAsync(active.Id, ct).ConfigureAwait(false);
        }

        if (!inNvim)
        {
            await TypeKickoff(folder, pane, ct).ConfigureAwait(false);
        }

        return Result<DispatchReply>.Ok(
            new DispatchReply(slug, folder, DispatchNote.Dispatched(slug, aidlc?.Profile)));
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
            var text = File.Exists(path) ? File.ReadAllText(path) : null;

            return text is not null && OrchestrationText.IsClassicAidlc(text) ? null : text;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static (string Prompt, (Profile Profile, ProfileSource Source)? Aidlc) ResolveAidlc(
        string prompt, AidlcMode mode, Profile projectDefault, Profile? argument)
    {
        if (mode == AidlcMode.Off)
        {
            return (prompt, null);
        }

        var (prefixed, task) = ProfilePrefix.Split(prompt);

        if (prefixed is { } fromPrefix)
        {
            return (task, (fromPrefix, ProfileSource.Prefix));
        }

        if (mode != AidlcMode.On)
        {
            return (prompt, null);
        }

        return argument is { } fromArgument
            ? (prompt, (fromArgument, ProfileSource.Argument))
            : (prompt, (projectDefault, ProfileSource.ProjectDefault));
    }

    private void StartIntent(string folder, IntakeRecord record)
    {
        if (intents is null)
        {
            return;
        }

        intents.Save(folder, record.State);

        foreach (var entry in record.Events)
        {
            intents.Append(folder, entry);
        }
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

    private static async Task LeaveKickoff(string folder, CancellationToken ct)
    {
        var inbox = Path.Combine(folder, ".fleet");
        Directory.CreateDirectory(inbox);

        await File.WriteAllTextAsync(Path.Combine(inbox, AgentHarness.InstructionSeenFile), "0", ct)
            .ConfigureAwait(false);

        await File.WriteAllTextAsync(
                Path.Combine(inbox, AgentHarness.AgentInstructionFile),
                AgentHarness.OrchestratorKickoff,
                ct)
            .ConfigureAwait(false);
    }

    private async Task TypeKickoff(string folder, PaneId pane, CancellationToken ct)
    {
        var marker = OrchestrationPaths.ReadyMarker(folder);

        var attempts = _pollInterval > TimeSpan.Zero
            ? (int)Math.Ceiling(_readyTimeout / _pollInterval)
            : 0;

        for (var i = 0; i < attempts && !File.Exists(marker); i++)
        {
            await Task.Delay(_pollInterval, ct).ConfigureAwait(false);
        }

        await mux.SendTextAsync(pane, AgentHarness.OrchestratorKickoff, ct).ConfigureAwait(false);
        await Task.Delay(TimeSpan.FromMilliseconds(400), ct).ConfigureAwait(false);
        await mux.SendTextAsync(pane, "\r", ct).ConfigureAwait(false);
    }
}
