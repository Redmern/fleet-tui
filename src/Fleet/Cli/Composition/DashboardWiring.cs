using DispatchRequest = Fleet.Features.Orchestrations.Dispatch.Models.DispatchCommand;
using Fleet.Features.Agents;
using Fleet.Features.Agents.AutoClose;
using Fleet.Features.Agents.AutoClose.Models;
using Fleet.Features.Agents.ChangeHarness;
using Fleet.Features.Agents.FinishAgent;
using Fleet.Features.Agents.HideAgent;
using Fleet.Features.Agents.ListAgents;
using Fleet.Features.Agents.NewAgent;
using Fleet.Features.Agents.NewAgent.Models;
using Fleet.Features.Agents.OpenAgent;
using Fleet.Features.Agents.OpenEditor;
using Fleet.Features.Agents.RemoveAgent;
using Fleet.Features.Agents.RemoveAgent.Models;
using Fleet.Features.Agents.RenameAgent;
using Fleet.Features.Agents.StopAgent;
using Fleet.Features.Dashboard.RebuildDashboard;
using Fleet.Features.Dashboard.ShowDashboard;
using Fleet.Features.Dashboard.ShowDashboard.Models;
using Fleet.Features.Diagnostics.ViewLogs;
using Fleet.Features.Files.BrowseFiles;
using Fleet.Features.Menu.EditKeybinds;
using Fleet.Features.Menu.EditSettings;
using Fleet.Features.Notifications.DetectNotices;
using Fleet.Features.Notifications.ShowNotices;
using Fleet.Features.Notifications.SyncNotices;
using Fleet.Features.Orchestrations.Dispatch;
using Fleet.Features.Orchestrations.ListSubs;
using Fleet.Features.Orchestrations.RenameOrchestration;
using Fleet.Features.Repositories;
using Fleet.Features.Repositories.AddRepository;
using Fleet.Features.Repositories.ListRemotes;
using Fleet.Features.Repositories.ListRepositories;
using Fleet.Features.Repositories.OpenRepository;
using Fleet.Features.Repositories.PullRepository;
using Fleet.Features.Repositories.RemoveRepository;
using Fleet.Features.Repositories.RemoveRepository.Models;
using Fleet.Features.Repositories.RenameRepository;
using Fleet.Features.Repositories.Secrets;
using Fleet.Features.Repositories.SetDefaultBranch;
using Fleet.Features.Setup.RunSetup;
using Fleet.Ports;
using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;
using Fleet.Ports.Approvals;
using Fleet.Ports.Approvals.Enums;
using Fleet.Ports.Git;
using Fleet.Ports.Keymap;
using Fleet.Ports.Mux;
using Fleet.Ports.Mux.Enums;
using Fleet.Ports.Mux.Models;
using Fleet.Ports.Notifications.Enums;
using Fleet.Ports.Notifications.Models;
using Fleet.Ports.Projects.Models;
using Fleet.Ports.Requests;
using Fleet.Ports.Settings;
using Fleet.Shared;
using Fleet.Shared.Constants;
using Fleet.Shared.Keymap;
using Fleet.Shared.Keymap.Enums;
using Fleet.Shared.Orchestrations;
using Fleet.Shared.Status;
using Fleet.Shared.Status.Models;
using Fleet.Ui;
using Terminal.Gui.App;

namespace Fleet.Cli.Composition;

public static class DashboardWiring
{
    private const int LogTail = 400;

    private static readonly FleetAction[] MenuActions =
    [
        FleetAction.NewAgent,
        FleetAction.ChangeHarness,
        FleetAction.ToggleHidden,
        FleetAction.RemoveAgent,
        FleetAction.AddRepository,
        FleetAction.RemoveRepository,
        FleetAction.Refresh,
        FleetAction.ViewLogs,
        FleetAction.BrowseFiles,
        FleetAction.RebuildDashboard,
        FleetAction.EditKeybinds,
        FleetAction.Close,
    ];

    private static Func<AgentRecord, BranchState> Peeked(BranchStates states) =>
        agent => states.Peek(agent.Worktree, agent.BaseRef);

    private static AgentRecord? At(ListAgentsHandler lister, string project, int tab, int index)
    {
        var listing = SubTree.Of(lister.Handle(project));

        var source = tab == DashboardTabs.SubsTab
            ? listing.Flat.Select(e => e.Agent).ToList()
            : (IReadOnlyList<AgentRecord>)listing.Board;

        return index >= 0 && index < source.Count ? source[index] : null;
    }

    private static string Label(AgentRecord agent) =>
        AgentHarness.IsOrchestrator(agent.Harness)
            ? agent.Branch
            : $"{agent.Repository}/{agent.Branch}";

    private static string? Noted(IFleetLog log, string project, string? message)
    {
        Note(log, project, message);

        return message;
    }

    private static void Note(IFleetLog log, string project, string? message)
    {
        if (message is { Length: > 0 })
        {
            log.Write(LogTag.For(project, message));
        }
    }

    private static string? Secrets(
        IApplication app,
        Keymap keymap,
        IMuxDriver mux,
        Project project,
        RepositoryChoice repository)
    {
        var secrets = new SecretsHandler();

        var plan = secrets.Plan(
            project.Root, repository.Name, repository.Directory, repository.DefaultBranch);

        return SecretsView.Show(
            app,
            keymap,
            plan,
            open: chosen => Edit(mux, project, chosen.Root),
            copy: chosen =>
            {
                if (chosen.Files.Count == 0)
                {
                    return $"{chosen.Repository} has no secret files to copy yet.";
                }

                var seeded = secrets.Distribute(chosen);

                return $"copied {chosen.Files.Count} secret file(s) into "
                     + $"{seeded} worktree(s) of {chosen.Repository}.";
            });
    }

    private static string? Edit(IMuxDriver mux, Project project, string root)
    {
        Directory.CreateDirectory(root);

        var panes = mux.ListPanesAsync().GetAwaiter().GetResult();

        var window = panes.FirstOrDefault(p => PathKey.Same(p.Cwd, project.Root))?.WindowId;

        var pane = mux.SpawnAsync(
                new SpawnOptions
                {
                    Cwd = root,
                    SessionName = project.Name,
                    WindowId = window,
                    Args = AgentHarness.BrowseCommand,
                })
            .GetAwaiter()
            .GetResult();

        if (pane.IsNone)
        {
            return $"could not open {root}.";
        }

        mux.SetTitleAsync(pane, "secrets").GetAwaiter().GetResult();

        return $"editing the secrets of {Path.GetFileName(Path.GetDirectoryName(root)!)}.";
    }

    private static string ChooseHarness(
        IApplication app,
        Keymap keymap,
        ChangeHarnessHandler harnesses,
        string project,
        AgentRecord agent)
    {
        var picked = FleetPicker.Choose(
            app,
            $"{agent.Repository}/{agent.Branch} opens",
            AgentHarness.All.Select(AgentHarness.Describe).ToList(),
            keymap,
            AgentHarness.All.ToList().IndexOf(agent.Harness));

        if (picked is null)
        {
            return string.Empty;
        }

        var wanted = AgentHarness.All[picked.Value];
        harnesses.Handle(project, agent, wanted);

        return $"{agent.Repository}/{agent.Branch} now opens {AgentHarness.Describe(wanted)} "
             + "next time it starts.";
    }

    private static IReadOnlyList<string> ReportLines(string folder)
    {
        var lines = new List<string>();

        void Append(string file)
        {
            try
            {
                if (File.Exists(file))
                {
                    if (lines.Count > 0)
                    {
                        lines.Add(string.Empty);
                        lines.Add($"--- {Path.GetFileName(file)} ---");
                        lines.Add(string.Empty);
                    }

                    lines.AddRange(File.ReadAllLines(file));
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                lines.Add($"could not read {file}: {e.Message}");
            }
        }

        Append(OrchestrationPaths.ReportFile(folder));

        var reports = OrchestrationPaths.ReportsFolder(folder);

        if (Directory.Exists(reports))
        {
            foreach (var file in Directory.EnumerateFiles(reports).Order())
            {
                Append(file);
            }
        }

        return lines.Count == 0 ? ["no report yet."] : lines;
    }

    private static string? RemoveInBackground(
        IAgentStore store,
        RemoveAgentHandler remover,
        IFleetLog log,
        string project,
        IReadOnlyList<(AgentRecord Agent, bool Delete)> doomed)
    {
        foreach (var (agent, _) in doomed)
        {
            store.Remove(project, agent.Worktree);
        }

        _ = Task.Run(async () =>
        {
            var failures = new List<string>();

            foreach (var (agent, delete) in doomed)
            {
                var gone = await remover.HandleAsync(project, agent, delete)
                    .ConfigureAwait(false);

                if (!gone.Succeeded)
                {
                    store.Save(project, agent);
                    failures.Add($"{Label(agent)}: {gone.Error}");
                }
            }

            var note = failures.Count == 0
                ? doomed.Count == 1
                    ? $"removed {Label(doomed[0].Agent)}."
                    : $"removed {doomed.Count} agent(s)."
                : $"could not remove: {string.Join("; ", failures)}";

            Note(log, project, note);
            Adapters.Notifier().Notify(note);
        });

        return Noted(log, project, doomed.Count == 1
            ? $"removing {Label(doomed[0].Agent)} in the background..."
            : $"removing {doomed.Count} agent(s) in the background...");
    }

    private static string? ToggleHidden(
        IMuxDriver mux,
        HideAgentHandler hider,
        Project project,
        AgentRecord agent,
        IReadOnlyList<Pane>? knownPanes = null)
    {
        var panes = knownPanes ?? mux.ListPanesAsync().GetAwaiter().GetResult();

        var dashboard = panes.FirstOrDefault(p => p.Id == mux.CurrentPane)?.WindowId
            ?? panes.FirstOrDefault(p => PathKey.Same(p.Cwd, project.Root))?.WindowId;

        var outcome = hider
            .HandleAsync(project.Name, agent, dashboard, panes)
            .GetAwaiter()
            .GetResult();

        if (!outcome.Succeeded)
        {
            return outcome.Error;
        }

        return outcome.Value!.Hidden
            ? $"{agent.Repository}/{agent.Branch} is hidden from the terminal; "
              + "it is still listed here."
            : $"{agent.Repository}/{agent.Branch} is back in the terminal.";
    }

    private static IReadOnlyList<string> RepositoryWarning(
        string name, string directory, RepositoryState state)
    {
        var lines = new List<string> { name, directory, string.Empty };

        if (!state.Exists)
        {
            lines.Add("It is already gone from disk.");
            return lines;
        }

        lines.Add("This deletes the repository and every worktree under it:");
        lines.AddRange(state.Worktrees.Take(5).Select(w => $"  {w}"));

        if (state.Worktrees.Count > 5)
        {
            lines.Add($"  ... and {state.Worktrees.Count - 5} more");
        }

        if (state.Unpushed.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add($"WARNING: {state.Unpushed.Count} branch(es) are not pushed:");
            lines.AddRange(state.Unpushed.Take(5).Select(b => $"  {b}"));
        }

        return lines;
    }

    private static IReadOnlyList<string> CascadeWarning(IReadOnlyList<AgentRecord> children)
    {
        var lines = new List<string>
        {
            "These agents were created by this sub-orchestrator.",
            "Removing them deletes their worktrees; uncommitted work is lost.",
            string.Empty,
        };

        lines.AddRange(children.Take(8).Select(c => $"  {c.Repository}/{c.Branch}"));

        if (children.Count > 8)
        {
            lines.Add($"  ... and {children.Count - 8} more");
        }

        return lines;
    }

    private static IReadOnlyList<string> RemovalWarning(
        AgentRecord agent, WorktreeState state, bool deleting)
    {
        var lines = new List<string>
        {
            Label(agent),
            agent.Worktree,
            string.Empty,
        };

        if (AgentHarness.IsOrchestrator(agent.Harness))
        {
            lines.Add(deleting
                ? "This deletes the orchestration folder and everything it holds:"
                : "The orchestration folder stays on disk; only fleet forgets it.");

            if (deleting)
            {
                lines.Add("  agent files, instructions, and reports.");
            }

            return lines;
        }

        if (!deleting)
        {
            lines.Add("The worktree and its branch stay on disk.");
            lines.Add("Only fleet forgets about this agent.");
            return lines;
        }

        if (!state.Exists)
        {
            lines.Add("Its worktree is already gone; only the record is removed.");
            return lines;
        }

        if (state.IsDirty)
        {
            lines.Add($"WARNING: {state.Changed.Count} uncommitted change(s) will be lost:");
            lines.AddRange(state.Changed.Take(5).Select(c => $"  {c}"));

            if (state.Changed.Count > 5)
            {
                lines.Add($"  ... and {state.Changed.Count - 5} more");
            }
        }
        else
        {
            lines.Add("The worktree is clean. Its branch is kept.");
        }

        return lines;
    }

    public static DashboardCallbacks For(
        IApplication app,
        Project project,
        Keymap keymap,
        IKeymapStore keymaps,
        IGitRunner git,
        IMuxDriver mux,
        IAgentStore agents,
        IActionRequestStore requests,
        IWorkspaceRequestStore workspaces,
        ISettingsStore settings,
        ISettingsSync settingsSync,
        IApprovalInbox approvals,
        IFleetLog log)
    {
        var repositories = new ListRepositoriesHandler(git);
        var remotes = new ListRemotesHandler(git);
        var adder = new AddRepositoryHandler(git);
        var lister = new ListAgentsHandler(agents);

        IReadOnlyList<Pane>? barPanes = null;
        var barPanesAt = DateTime.MinValue;
        var activity = new PaneActivity(mux);
        var paneTexts = activity.Texts;

        IReadOnlyList<AgentRecord> WithBarState(IReadOnlyList<AgentRecord> records)
        {
            var now = DateTime.UtcNow;

            if (barPanes is null || now - barPanesAt > TimeSpan.FromMilliseconds(300))
            {
                barPanes = mux.ListPanesAsync().GetAwaiter().GetResult();
                barPanesAt = now;
            }

            var panes = barPanes;

            return [.. records.Select(a => a with { Hidden = !AgentPanes.Shown(a, panes) })];
        }

        var agentStates = Adapters.AgentStates();
        AgentSnapshot? hookSnapshot = null;
        var hookSnapshotAt = DateTime.MinValue;

        Func<AgentRecord, AgentReport?> HookedNow()
        {
            var now = DateTime.UtcNow;

            if (hookSnapshot is null || now - hookSnapshotAt > TimeSpan.FromMilliseconds(500))
            {
                hookSnapshot = settings.Load(project.Name).StatusHooks
                    ? agentStates.GetSnapshotAsync().GetAwaiter().GetResult()
                    : AgentSnapshot.Empty;
                hookSnapshotAt = now;
            }

            var snapshot = hookSnapshot;

            return agent => AgentStatusRules.For(snapshot, agent.Worktree, now, AgentStatusRules.StallAfter);
        }

        AgentReport? Hooked(AgentRecord agent) => HookedNow()(agent);

        IReadOnlyList<AgentRecord> WithActivity(IReadOnlyList<AgentRecord> records) =>
            barPanes is null ? records : activity.For(records, barPanes, HookedNow());

        var spawner = new NewAgentHandler(git, mux, agents);
        var hider = new HideAgentHandler(mux, agents);
        var editor = new OpenEditorHandler(mux);
        var branches = new ListBranchesHandler(git);
        var harnesses = new ChangeHarnessHandler(agents);
        var stopper = new StopAgentHandler(mux, agents);
        var remover = new RemoveAgentHandler(git, mux, agents);
        var agentRenamer = new RenameAgentHandler(git, agents);
        var subRenamer = new RenameOrchestrationHandler(agents, mux);
        var repoRenamer = new RenameRepositoryHandler();
        var repositoryRemover = new RemoveRepositoryHandler(git);
        var puller = new PullRepositoryHandler(git);
        var defaults = new SetDefaultBranchHandler(git);
        var opener2 = new OpenRepositoryHandler(mux);
        var branches0 = branches;
        var states = new BranchStates(git);

        var signalCounter = 0L;

        void DrainFile(string file, string name)
        {
            try
            {
                if (!File.Exists(file))
                {
                    return;
                }

                var text = File.ReadAllText(file);

                File.Delete(file);

                if (text.Trim().Length > 0)
                {
                    Adapters.EmitUserVar(name, $"{++signalCounter}\n{text}");
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }

        void DrainSignals()
        {
            DrainFile(Adapters.NotifyFile, "fleet-notify");
            DrainFile(Adapters.WorkspaceFile, "fleet-workspace");
        }

        async Task<string?> OpenFlow(AgentRecord agent)
        {
            var inNvim = settings.Load(project.Name).SubOrchestratorsInNvim;
            var executable = AgentHarness.CommandFor(agent.Harness, orchestratorInNvim: inNvim)[0];

            if (!Adapters.OnPath(executable))
            {
                return Noted(log, project.Name, HarnessTrouble.Missing(executable));
            }

            ClaudeWiring.TrustFolder(agent.Worktree);

            var outcome = await new OpenAgentHandler(mux, agents, inNvim)
                .HandleAsync(project.Name, agent, project.Root)
                .ConfigureAwait(false);

            Note(log, project.Name, outcome.Succeeded
                ? $"opened {Label(agent)}"
                : $"could not open {Label(agent)}: {outcome.Error}");

            return outcome.Succeeded ? null : outcome.Error;
        }

        var noticeStore = Adapters.Notices();
        var toldOpen = -1;
        var toldAt = DateTime.MinValue;
        var seenAlive = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var quiet = new Dictionary<string, (string Text, DateTime Since)>(StringComparer.OrdinalIgnoreCase);
        var againstBase = new System.Collections.Concurrent.ConcurrentDictionary<string, (DateTime At, int Behind, bool Conflicts)>(StringComparer.OrdinalIgnoreCase);
        var checking = new System.Collections.Concurrent.ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        var idleSeen = new Dictionary<string, (string Text, DateTime Since)>(StringComparer.OrdinalIgnoreCase);
        var closingIdle = 0;

        (int Behind, bool Conflicts) AgainstBase(AgentRecord agent, DateTime now)
        {
            if (agent.BaseRef.Length == 0 || !Directory.Exists(agent.Worktree))
            {
                return (0, false);
            }

            var known = againstBase.TryGetValue(agent.Worktree, out var last);
            if ((!known || now - last.At > NoticeChecks.BaseEvery) && checking.TryAdd(agent.Worktree, 0))
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var behind = await git.RunAsync(agent.Worktree, ["rev-list", "--count", $"HEAD..{agent.BaseRef}"]).ConfigureAwait(false);
                        var merge = await git.RunAsync(agent.Worktree, ["merge-tree", "--write-tree", "--quiet", agent.BaseRef, "HEAD"]).ConfigureAwait(false);
                        againstBase[agent.Worktree] = (
                            DateTime.UtcNow,
                            behind.Ok && int.TryParse(behind.Out.Trim(), out var count) ? count : 0,
                            merge.ExitCode == 1);
                    }
                    finally
                    {
                        checking.TryRemove(agent.Worktree, out _);
                    }
                });
            }

            return known ? (last.Behind, last.Conflicts) : (0, false);
        }

        NoticeBoard Board(IReadOnlyList<Notice> all, DateTime now) => DashboardSkeleton.Notices(all, now);

        NoticeBoard LoadNotices()
        {
            var now = DateTime.UtcNow;
            var panes = barPanes ?? mux.ListPanesAsync().GetAwaiter().GetResult();

            var watches = lister.Handle(project.Name).Select(agent =>
            {
                var alive = panes.Any(p => AgentPanes.Owns(p, agent) && !SubBrowse.Is(p));
                if (alive)
                {
                    seenAlive.Add(agent.Worktree);
                }

                var text = alive ? paneTexts.GetValueOrDefault(agent.Worktree, string.Empty) : string.Empty;
                var settled = NoticeDetector.Settled(text);
                if (!quiet.TryGetValue(agent.Worktree, out var still) || still.Text != settled)
                {
                    quiet[agent.Worktree] = still = (settled, now);
                }

                var (behind, conflicts) = AgainstBase(agent, now);
                return new AgentWatch(
                    agent, alive, text, now - still.Since, behind, conflicts,
                    PaneLost: !alive && seenAlive.Contains(agent.Worktree),
                    Hooked: Hooked(agent));
            }).ToList();

            var stored = noticeStore.Load(project.Name);
            var next = NoticeSync.Apply(stored, NoticeDetector.Detect(project.Name, watches, now), now);

            if (!stored.Select(Signature).SequenceEqual(next.Select(Signature)))
            {
                noticeStore.Save(project.Name, next);
            }

            Alert(next.Count(n => n.IsOpen), NoticeSync.Fresh(stored, next));
            CloseIdle(next, panes, now);
            return Board(next, now);
        }

        void CloseIdle(IReadOnlyList<Notice> notices, IReadOnlyList<Pane> panes, DateTime now)
        {
            var config = settings.Load(project.Name);

            if (!config.AutoClose || Interlocked.Exchange(ref closingIdle, 1) == 1)
            {
                return;
            }

            try
            {
                foreach (var agent in lister.Handle(project.Name).Where(a => IdleAgents.Finished(a.Status)))
                {
                    var owned = IdleAgents.PanesOf(agent, panes);

                    if (owned.Count == 0)
                    {
                        idleSeen.Remove(agent.Worktree);
                        continue;
                    }

                    var text = mux.GetTextAsync(owned[0].Id).GetAwaiter().GetResult();
                    var settled = NoticeDetector.Settled(text);
                    if (!idleSeen.TryGetValue(agent.Worktree, out var seen) || seen.Text != settled)
                    {
                        idleSeen[agent.Worktree] = seen = (settled, now);
                    }

                    var live = AgentActivity.Classify(text);
                    var mine = notices.Where(n => n.Resolved is null && PathKey.Same(n.Worktree, agent.Worktree)).ToList();
                    var reported = mine
                        .Where(n => n.Kind is NoticeKind.Done or NoticeKind.Failed)
                        .Select(n => n.Since)
                        .DefaultIfEmpty(now)
                        .Max();

                    var watch = new IdleWatch(
                        agent,
                        PaneAlive: true,
                        Focused: IdleAgents.Focused(owned),
                        Working: live == AgentActivity.Working,
                        AsksTheUser: live == AgentActivity.Waiting
                            || mine.Any(n => n.Kind is NoticeKind.Permission or NoticeKind.NeedsInput),
                        reported > seen.Since ? reported : seen.Since);

                    if (!IdleAgents.ShouldClose(watch, config, project.Root, now))
                    {
                        continue;
                    }

                    var stopped = stopper.HandleAsync(project.Name, agent).GetAwaiter().GetResult();
                    idleSeen.Remove(agent.Worktree);

                    Note(log, project.Name, stopped.Succeeded
                        ? IdleAgents.Note(Label(agent), agent.Status, now - watch.LastActivity)
                        : $"could not auto-close {Label(agent)}: {stopped.Error}");
                }
            }
            finally
            {
                Volatile.Write(ref closingIdle, 0);
            }
        }

        void Alert(int open, IReadOnlyList<Notice> fresh)
        {
            if (open == toldOpen && fresh.Count == 0 && DateTime.UtcNow - toldAt < NoticeChecks.RetellEvery)
            {
                return;
            }

            toldOpen = open;
            toldAt = DateTime.UtcNow;
            var settings = noticeStore.Settings();
            var embedded = mux.Name == "embedded";

            _ = Task.Run(() =>
            {
                var inWindow = true;

                if (embedded)
                {
                    inWindow = EmbeddedWiring.Notices(project.Name, open, settings.Bell && fresh.Count > 0);
                }
                else if (settings.Bell && fresh.Count > 0)
                {
                    Console.Out.Write('\a');
                    Console.Out.Flush();
                }

                if (settings.Toast && fresh.Count > 0 && inWindow)
                {
                    Platform.Notifications.DesktopToast.Show($"fleet · {project.Name}", NoticeSync.Summary(fresh));
                }
            });
        }

        static string Signature(Notice n) => $"{n.Key}|{n.Message}|{n.Since:O}|{n.Resolved:O}|{n.Dismissed:O}";

        async Task<string?> OpenNotice(string worktree)
        {
            var agent = lister.Handle(project.Name).FirstOrDefault(a => PathKey.Same(a.Worktree, worktree));
            return agent is null ? $"that agent is gone ({worktree})" : await OpenFlow(agent).ConfigureAwait(false);
        }

        return new DashboardCallbacks(
            LoadRepositories: async () =>
            {
                var found = (await repositories.HandleAsync(project.Root).ConfigureAwait(false))
                    .Select(r => new RepositoryChoice(r.Name, r.Path, r.DefaultBranch))
                    .ToList();

                return (IReadOnlyList<RepositoryChoice>)found;
            },

            AddRepository: async () =>
            {
                var known = await remotes
                    .HandleAsync([.. (await repositories.HandleAsync(project.Root)
                        .ConfigureAwait(false)).Select(r => r.Path)])
                    .ConfigureAwait(false);

                var request = await FleetAsync
                    .OnUi(app, () => AddRepositoryView.Show(app, project.Root, known, keymap))
                    .ConfigureAwait(false);

                if (request is null)
                {
                    return null;
                }

                var outcome = await adder.HandleAsync(request).ConfigureAwait(false);

                Note(log, project.Name, outcome.Succeeded
                    ? $"added repository {request.Name}"
                    : $"could not add repository {request.Name}: {outcome.Error}");

                return outcome.Succeeded ? null : outcome.Error;
            },

            ShowMenu: () =>
                mux.Caps.HasFlag(MuxCaps.Popup) && !mux.CurrentPane.IsNone
                && EmbeddedWiring.OpenMenu(null)
                    ? FleetAction.None
                    : FleetUi.Menu(app, keymap, MenuActions),

            EditKeybinds: () => new Keymap(EditKeybindsView.Show(app, keymaps, keymap)),

            ReloadKeymap: () => new Keymap(keymaps.Load()),

            EditSettings: () => EditSettingsView.Show(
                app,
                keymap,
                project.Name,
                settings.Load(project.Name),
                next =>
                {
                    settings.Save(project.Name, next);
                    var synced = settingsSync.Resync(project.Name, project.Root, next);
                    return synced.Succeeded
                        ? null
                        : $"Saved, but claude's permissions could not be updated: {synced.Error}";
                }),

            BrowseFiles: () => Adapters.OnPath(FileBrowser.Command)
                ? Noted(
                    log,
                    project.Name,
                    Adapters.BrowseFolder(mux, project.Name, project.Root) is not null
                        ? $"file navigator opened in {project.Root}."
                        : $"could not open {FileBrowser.Command}.")
                : Noted(log, project.Name, HarnessTrouble.Missing(FileBrowser.Command)),

            RebuildDashboard: () =>
            {
                var rebuilt = new RebuildDashboardHandler(mux, settings.Load(project.Name).MainOrchestratorInNvim)
                    .HandleAsync(project.Root, lister.Handle(project.Name), AgentHarness.Orchestrator)
                    .GetAwaiter()
                    .GetResult();

                return Noted(log, project.Name, rebuilt.Succeeded ? rebuilt.Value : rebuilt.Error);
            },

            ShowLogs: () => ViewLogsView.Show(
                app,
                keymap,
                project.Name,
                LogParser.For(project.Name, LogParser.Parse(log.Tail(LogTail)))),

            TakeRequest: () => requests.TakePending(project.Name),

            LoadAgents: () =>
            {
                var board = WithActivity(SubTree.Of(WithBarState(lister.Handle(project.Name))).Board);

                return new AgentBoard(
                    AgentRows.For(board, Peeked(states)),
                    board.Count,
                    [.. board.Select(a => a.Hidden)],
                    [.. board.Select(a => a.Status)]);
            },

            LoadSubs: () =>
            {
                var shown = SubTree.Of(WithBarState(lister.Handle(project.Name)));
                var listing = SubTree.Of([.. shown.Board, .. WithActivity([.. shown.Flat.Select(e => e.Agent)])]);
                var trigger = settings.Load(project.Name).Trigger;

                return new SubBoard(
                    SubRows.For(listing, Peeked(states), trigger),
                    listing.Flat.Count(e => !e.IsChild),
                    [.. listing.Flat.Select(e => e.Agent.Hidden)],
                    SubRows.GapsAfter(listing));
            },

            NewAgent: async (available, selected) =>
            {
                var request = NewAgentView.Show(
                    app,
                    new NewAgentPrompt(
                        project.Name,
                        available.Select(r => (r.Name, r.Directory, r.DefaultBranch)).ToList(),
                        selected,
                        directory => branches.HandleAsync(directory).GetAwaiter().GetResult(),
                        AgentHarness.Nvim),
                    keymap);

                if (request is null)
                {
                    return null;
                }

                if (!Adapters.OnPath(request.Harness))
                {
                    return Noted(log, project.Name, HarnessTrouble.Missing(request.Harness));
                }

                var outcome = await spawner.HandleAsync(request).ConfigureAwait(false);

                if (outcome.Succeeded)
                {
                    ClaudeWiring.ApproveFolder(
                        project.Name, outcome.Value!.Worktree,
                        outcome.Value.Repository, outcome.Value.Branch);
                }

                Note(log, project.Name, outcome.Succeeded
                    ? $"started agent {request.RepositoryName}/{outcome.Value!.Branch}"
                    : $"could not start an agent in {request.RepositoryName}: {outcome.Error}");

                return outcome.Succeeded ? null : outcome.Error;
            },

            DispatchSub: async () =>
            {
                var history = Adapters.History();

                var prompt = await FleetAsync
                    .OnUi(app, () =>
                    {
                        var past = history.List(project.Name);
                        var initial = string.Empty;

                        if (past.Count > 0)
                        {
                            var options = past.Append("Type a new task...").ToList();
                            var picked = FleetPicker.Choose(app, "New sub-orchestrator", options, keymap);

                            if (picked is null)
                            {
                                return null;
                            }

                            if (picked.Value < past.Count)
                            {
                                initial = past[picked.Value];
                            }
                        }

                        return FleetPrompt.Text(
                            app, "New sub-orchestrator", initial, "Task for the sub");
                    })
                    .ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(prompt))
                {
                    return null;
                }

                var reply = await new DispatchHandler(
                        mux, agents, Adapters.HarnessConfig(),
                        history: history, namer: Adapters.SlugNamer(), settings: Adapters.Settings(), intents: Adapters.Intents())
                    .HandleAsync(
                        new DispatchRequest(project.Name, project.Root, prompt),
                        DateTimeOffset.UtcNow.ToString("O"))
                    .ConfigureAwait(false);

                Note(log, project.Name, reply.Succeeded
                    ? $"dispatched sub-orchestrator {reply.Value!.Slug}"
                    : $"could not dispatch: {reply.Error}");

                return reply.Succeeded ? null : reply.Error;
            },

            OpenAgent: async (tab, index) =>
            {
                var agent = At(lister, project.Name, tab, index);

                return agent is null ? null : await OpenFlow(agent).ConfigureAwait(false);
            },

            BatchAgents: async (tab, indexes, choice) =>
            {
                var picked = indexes
                    .Select(i => At(lister, project.Name, tab, i))
                    .OfType<AgentRecord>()
                    .ToList();

                if (choice == "forget")
                {
                    return RemoveInBackground(
                        agents,
                        remover,
                        log,
                        project.Name,
                        [.. picked.Select(a => (a, false))]);
                }

                var failures = new List<string>();

                foreach (var agent in picked)
                {
                    switch (choice)
                    {
                        case "hide":
                            ToggleHidden(mux, hider, project, agent);
                            break;

                        case "stop":
                            {
                                var stopped = await stopper.HandleAsync(project.Name, agent)
                                    .ConfigureAwait(false);

                                if (!stopped.Succeeded)
                                {
                                    failures.Add($"{agent.Branch}: {stopped.Error}");
                                }

                                break;
                            }

                        case "forget":
                            {
                                var removed = await remover
                                    .HandleAsync(project.Name, agent, deleteWorktree: false)
                                    .ConfigureAwait(false);

                                if (!removed.Succeeded)
                                {
                                    failures.Add($"{agent.Branch}: {removed.Error}");
                                }

                                break;
                            }
                    }
                }

                return Noted(log, project.Name, failures.Count == 0
                    ? $"batch {choice}: {picked.Count} agent(s)."
                    : $"batch {choice}: {string.Join("; ", failures)}");
            },

            HideAgent: (tab, index) =>
            {
                var agent = At(lister, project.Name, tab, index);


                if (agent is null)
                {
                    return null;
                }

                var panes = mux.ListPanesAsync().GetAwaiter().GetResult();

                if (panes.Any(p => AgentPanes.Owns(p, agent)))
                {
                    return Noted(log, project.Name, ToggleHidden(mux, hider, project, agent, panes));
                }

                var active = panes.FirstOrDefault(p => p.IsActive);
                var message = OpenFlow(agent).GetAwaiter().GetResult();

                if (active is not null)
                {
                    mux.FocusPaneAsync(active.Id).GetAwaiter().GetResult();
                }

                return message;
            },

            ManageAgent: async (tab, index) =>
            {
                var agent = At(lister, project.Name, tab, index);

                if (agent is null)
                {
                    return null;
                }

                var entries = AgentDisposal.For(agent.Hidden, AgentHarness.IsOrchestrator(agent.Harness));

                var picked = await FleetAsync
                    .OnUi(app, () => FleetPicker.Choose(app, Label(agent), entries, keymap))
                    .ConfigureAwait(false);

                if (picked is null)
                {
                    return null;
                }

                var choice = entries[picked.Value].Key;

                if (choice == "o")
                {
                    return Noted(
                        log, project.Name, await FleetAsync
                            .OnUi(app, () => ChooseHarness(app, keymap, harnesses, project.Name, agent))
                            .ConfigureAwait(false));
                }

                if (choice == "p")
                {
                    var baseBranch = FinishAgentHandler.LocalBase(agent);

                    var go = await FleetAsync
                        .OnUi(app, () => FleetDialog.Confirm(
                            app,
                            $"Finish {Label(agent)}?",
                            [$"Merges {agent.Branch} into {baseBranch}."],
                            "Merge"))
                        .ConfigureAwait(false);

                    if (!go)
                    {
                        return null;
                    }

                    var pushIt = await FleetAsync
                        .OnUi(app, () => FleetDialog.Confirm(
                            app, $"Push {baseBranch} to origin afterwards?", [], "Push"))
                        .ConfigureAwait(false);

                    var finished = await new FinishAgentHandler(git)
                        .HandleAsync(agent, pushIt)
                        .ConfigureAwait(false);

                    if (!finished.Succeeded)
                    {
                        return Noted(log, project.Name, finished.Error);
                    }

                    var cleanup = await FleetAsync
                        .OnUi(app, () => FleetDialog.Confirm(
                            app,
                            "Remove the agent and delete its worktree?",
                            [finished.Value!],
                            "Remove"))
                        .ConfigureAwait(false);

                    if (!cleanup)
                    {
                        return Noted(log, project.Name, finished.Value);
                    }

                    var gone = await remover
                        .HandleAsync(project.Name, agent, deleteWorktree: true)
                        .ConfigureAwait(false);

                    return Noted(log, project.Name, gone.Succeeded
                        ? $"{finished.Value} Agent removed with its worktree."
                        : $"{finished.Value} But removal failed: {gone.Error}");
                }

                if (choice == "v")
                {
                    await FleetAsync
                        .OnUi(app, () =>
                        {
                            FleetTextView.Show(
                                app,
                                $"report — {agent.Branch}",
                                ReportLines(agent.Worktree),
                                keymap);

                            return true;
                        })
                        .ConfigureAwait(false);

                    return null;
                }

                if (choice == "h")
                {
                    return Noted(log, project.Name, ToggleHidden(mux, hider, project, agent));
                }

                if (choice == "r")
                {
                    var panes = await mux.ListPanesAsync().ConfigureAwait(false);

                    if (!AgentHarness.IsOrchestrator(agent.Harness)
                        && panes.Any(p => AgentPanes.Owns(p, agent)))
                    {
                        return Noted(
                            log, project.Name,
                            $"Close {Label(agent)} before renaming it; it is still running, maybe hidden.");
                    }

                    var typed = await FleetAsync
                        .OnUi(app, () => FleetPrompt.Text(app, $"Rename {Label(agent)}", agent.Branch))
                        .ConfigureAwait(false);

                    if (typed is null)
                    {
                        return null;
                    }

                    if (AgentHarness.IsOrchestrator(agent.Harness))
                    {
                        var slug = OrchestrationSlug.Of(typed);

                        var sub = await subRenamer.HandleAsync(project.Name, agent, slug)
                            .ConfigureAwait(false);

                        if (!sub.Succeeded)
                        {
                            return Noted(log, project.Name, sub.Error);
                        }

                        ClaudeWiring.SyncFolder(project.Name, sub.Value!.Worktree, slug);
                        ClaudeWiring.TrustFolder(sub.Value!.Worktree);

                        return Noted(log, project.Name, $"{agent.Branch} renamed to {slug}.");
                    }

                    var planned = AgentBranch.Plan(typed, string.Empty);

                    if (!planned.Succeeded)
                    {
                        return Noted(log, project.Name, planned.Error);
                    }

                    var renamedAgent = await agentRenamer
                        .HandleAsync(project.Name, agent, planned.Value!.Branch)
                        .ConfigureAwait(false);

                    if (!renamedAgent.Succeeded)
                    {
                        return Noted(log, project.Name, renamedAgent.Error);
                    }

                    ClaudeWiring.ApproveFolder(
                        project.Name, renamedAgent.Value!.Worktree,
                        renamedAgent.Value.Repository, renamedAgent.Value.Branch);

                    return Noted(log, project.Name, $"{Label(agent)} renamed to {renamedAgent.Value.Branch}.");
                }

                if (choice == "s")
                {
                    var stopped = await stopper.HandleAsync(project.Name, agent).ConfigureAwait(false);

                    return Noted(log, project.Name, stopped.Succeeded
                        ? $"{Label(agent)} stopped; its worktree is untouched."
                        : stopped.Error);
                }

                var deleting = choice == "d";

                var state = deleting
                    ? await remover.InspectAsync(agent).ConfigureAwait(false)
                    : WorktreeState.Gone;

                var confirmed = await FleetAsync
                    .OnUi(app, () => FleetDialog.Confirm(
                        app,
                        deleting ? "Delete this worktree?" : "Remove this agent?",
                        RemovalWarning(agent, state, deleting),
                        confirmText: deleting ? "Delete" : "Remove"))
                    .ConfigureAwait(false);

                if (!confirmed)
                {
                    return null;
                }

                var doomed = new List<(AgentRecord Agent, bool Delete)>();

                if (AgentHarness.IsOrchestrator(agent.Harness))
                {
                    var children = SubChildren.Of(lister.Handle(project.Name), agent.Branch);

                    var cascade = children.Count > 0 && await FleetAsync
                        .OnUi(app, () => FleetDialog.Confirm(
                            app,
                            $"Throw away its {children.Count} agent(s)?",
                            CascadeWarning(children),
                            confirmText: "Remove all"))
                        .ConfigureAwait(false);

                    if (cascade)
                    {
                        doomed.AddRange(children.Select(c => (c, true)));
                    }
                    else
                    {
                        foreach (var child in children)
                        {
                            agents.Save(project.Name, SubChildren.Released(child));
                        }
                    }
                }

                doomed.Add((agent, deleting));

                return RemoveInBackground(agents, remover, log, project.Name, doomed);
            },

            RemoveRepository: async repository =>
            {
                var owned = lister.Handle(project.Name)
                    .Where(a => a.Repository == repository.Name)
                    .ToList();

                if (owned.Count > 0)
                {
                    return $"{repository.Name} still has {owned.Count} agent(s). "
                         + "Remove those first.";
                }

                var state = await repositoryRemover
                    .InspectAsync(repository.Directory)
                    .ConfigureAwait(false);

                var confirmed = await FleetAsync
                    .OnUi(app, () => FleetDialog.Confirm(
                        app,
                        "Delete this repository?",
                        RepositoryWarning(repository.Name, repository.Directory, state),
                        confirmText: "Delete"))
                    .ConfigureAwait(false);

                if (!confirmed)
                {
                    return null;
                }

                var outcome = repositoryRemover.Handle(repository.Directory);

                return Noted(log, project.Name, outcome.Succeeded
                    ? $"{repository.Name} deleted."
                    : outcome.Error);
            },

            PullRepository: async repository =>
            {
                var outcome = await puller
                    .HandleAsync(repository.Directory, repository.DefaultBranch)
                    .ConfigureAwait(false);

                return Noted(log, project.Name, outcome.Succeeded
                    ? $"{repository.Name}: {outcome.Value}"
                    : outcome.Error);
            },

            ManageRepository: async repository =>
            {
                var picked = await FleetAsync
                    .OnUi(app, () => FleetPicker.Choose(
                        app, repository.Name, RepositoryChores.Entries, keymap))
                    .ConfigureAwait(false);

                if (picked == RepositoryChores.Pull)
                {
                    return RepositoryManaged.Then(FleetAction.PullRepository);
                }

                if (picked == RepositoryChores.Remove)
                {
                    return RepositoryManaged.Then(FleetAction.RemoveRepository);
                }

                if (picked == RepositoryChores.Secrets)
                {
                    return new RepositoryManaged(
                        Noted(log, project.Name, await FleetAsync
                            .OnUi(app, () => Secrets(app, keymap, mux, project, repository))
                            .ConfigureAwait(false)));
                }

                if (picked == RepositoryChores.Rename)
                {
                    var owned = lister.Handle(project.Name)
                        .Count(a => a.Repository == repository.Name);

                    if (owned > 0)
                    {
                        return new RepositoryManaged(
                            $"{repository.Name} still has {owned} agent(s). Remove those first.");
                    }

                    var name = await FleetAsync
                        .OnUi(app, () => FleetPrompt.Text(app, $"Rename {repository.Name}", repository.Name))
                        .ConfigureAwait(false);

                    if (name is null)
                    {
                        return RepositoryManaged.Nothing;
                    }

                    var outcome = repoRenamer.Handle(project.Root, repository.Directory, name);

                    return new RepositoryManaged(Noted(log, project.Name, outcome.Succeeded
                        ? $"{repository.Name} renamed to {name.Trim()}."
                        : outcome.Error));
                }

                if (picked != RepositoryChores.DefaultBranch)
                {
                    return RepositoryManaged.Nothing;
                }

                var branches = (await branches0.HandleAsync(repository.Directory).ConfigureAwait(false))
                    .Where(b => !b.IsRemote)
                    .ToList();

                if (branches.Count == 0)
                {
                    return new RepositoryManaged($"{repository.Name} has no local branches.");
                }

                var chosen = await FleetAsync
                    .OnUi(app, () => FleetPicker.Choose(
                        app,
                        $"{repository.Name} default branch",
                        branches.Select(b => b.Reference).ToList(),
                        keymap,
                        branches.FindIndex(b => b.Reference == repository.DefaultBranch)))
                    .ConfigureAwait(false);

                if (chosen is null)
                {
                    return RepositoryManaged.Nothing;
                }

                var wanted = branches[chosen.Value].Reference;

                var set = await defaults
                    .HandleAsync(repository.Directory, wanted)
                    .ConfigureAwait(false);

                return new RepositoryManaged(Noted(log, project.Name, set.Succeeded
                    ? $"{repository.Name} now defaults to {wanted}. Its worktrees are untouched."
                    : set.Error));
            },

            OpenRepository: async repository =>
            {
                var outcome = await opener2
                    .HandleAsync(
                        project.Name,
                        repository.Name,
                        repository.Directory,
                        repository.DefaultBranch,
                        project.Root)
                    .ConfigureAwait(false);

                return outcome.Succeeded ? null : outcome.Error;
            },

            AgentState: agent => states.For(agent.Worktree, agent.BaseRef),

            RepositoryState: repository => states.Peek(
                RepositoryWorktree.For(
                    repository.Directory, repository.DefaultBranch, Directory.Exists)),

            TakeApproval: () => approvals.TakePending(project.Name),

            AnswerApproval: (id, allowed) => approvals.Answer(
                project.Name,
                id,
                allowed ? ApprovalDecision.Allowed : ApprovalDecision.Denied),

            Heartbeat: () => approvals.Heartbeat(project.Name),

            DrainSignals: DrainSignals,

            LoadNotices: LoadNotices,

            DismissNotices: keys =>
                noticeStore.Save(project.Name, NoticeSync.Dismiss(noticeStore.Load(project.Name), [.. keys], DateTime.UtcNow)),

            OpenNotice: OpenNotice,

            OpenEditor: async (tab, index) =>
            {
                var agent = At(lister, project.Name, tab, index);

                if (agent is null)
                {
                    return null;
                }

                var outcome = await editor.HandleAsync(project.Name, agent, project.Root)
                    .ConfigureAwait(false);

                return outcome.Succeeded
                    ? null
                    : Noted(log, project.Name, $"could not open an editor for {Label(agent)}: {outcome.Error}");
            },

            LoadRepositoriesFast: () => DashboardSkeleton.Repositories(project.Root),

            LoadAgentsFast: () => DashboardSkeleton.Agents(lister.Handle(project.Name)),

            LoadSubsFast: () => DashboardSkeleton.Subs(
                lister.Handle(project.Name), settings.Load(project.Name).Trigger),

            LoadNoticesFast: () => DashboardSkeleton.Notices(noticeStore.Load(project.Name), DateTime.UtcNow));
    }
}
