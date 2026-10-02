using Fleet.Cli.Composition;
using Fleet.Features.Menu.EditKeybinds;
using Fleet.Features.Projects.CreateProject;
using Fleet.Features.Projects.OpenProject;
using Fleet.Features.Projects.OpenProject.Models;
using Fleet.Features.Projects.PickProject;
using Fleet.Features.Agents.ListAgents;
using Fleet.Features.Files.BrowseFiles;
using Fleet.Features.Projects.PickProject.Models;
using Fleet.Features.Projects.RestoreSession;
using Fleet.Features.Projects.RemoveProject;
using Fleet.Features.Projects.LocateProject;
using Fleet.Features.Projects.SwitchProject;
using Fleet.Features.Remotes.ManageRemotes;
using Fleet.Ports.Sessions.Models;
using Fleet.Ports;
using Fleet.Ports.Mux;
using Fleet.Ports.Projects.Models;
using Fleet.Shared;
using Fleet.Shared.Constants;
using Fleet.Shared.Keymap.Enums;
using Fleet.Ui;
using Fleet.Ui.Enums;
using Terminal.Gui.App;

namespace Fleet.Cli.Commands;

public static class PickProjectCommand
{
    private static readonly FleetAction[] MenuActions =
    [
        FleetAction.NewProject,
        FleetAction.OpenProject,
        FleetAction.RemoveProject,
        FleetAction.EditKeybinds,
        FleetAction.Close,
    ];

    public static async Task<int> RunAsync()
    {
        var log = Adapters.Log();
        var mux = Adapters.Mux(log);

        if (mux.Unsupported is not null)
        {
            return Fail(mux.Unsupported);
        }

        var picked = Choose();

        if (picked is null)
        {
            return 0;
        }

        if (picked.Session is { } session)
        {
            return await OpenSessionAsync(mux.Driver, session, log).ConfigureAwait(false);
        }

        var chosen = picked.Project!;

        if (SwitchProjectHandler.Applies(mux.Driver))
        {
            return await OpenAsWorkspaceAsync(mux.Driver, chosen, log).ConfigureAwait(false);
        }

        var newWindow = picked.NewWindow;
        string? windowId = null;

        if (!newWindow)
        {
            var resolved = await ResolveWindowAsync(mux.Driver).ConfigureAwait(false);

            if (resolved is null)
            {
                return 0;
            }

            newWindow = resolved.Value.NewWindow;
            windowId = resolved.Value.WindowId;
        }

        var result = await new OpenProjectHandler(mux.Driver)
            .HandleAsync(new OpenProjectCommand(
                chosen, AgentHarness.Orchestrator, Adapters.Executable, windowId, Adapters.MainOrchestratorInNvim(chosen.Name)))
            .ConfigureAwait(false);

        if (!result.Succeeded)
        {
            return Fail(result.Error!);
        }

        var agents = new ListAgentsHandler(Adapters.Agents()).Handle(chosen.Name);

        var runnable = agents
            .Where(a => Adapters.OnPath(
                AgentHarness.CommandFor(a.Harness, orchestratorInNvim: Adapters.SubOrchestratorsInNvim(chosen.Name))[0]))
            .ToList();

        foreach (var stranded in agents.Except(runnable)
            .Select(a => AgentHarness.CommandFor(
                a.Harness, orchestratorInNvim: Adapters.SubOrchestratorsInNvim(chosen.Name))[0])
            .Distinct())
        {
            Console.Error.WriteLine(
                $"fleet: {stranded} is not on PATH, so agents that open it stay closed.");
        }

        await new RestoreSessionHandler(mux.Driver, Adapters.SubOrchestratorsInNvim(chosen.Name), Adapters.Agents())
            .HandleAsync(chosen.Name, chosen.Root, runnable)
            .ConfigureAwait(false);

        await mux.Driver.FocusPaneAsync(result.Value.DashPane).ConfigureAwait(false);

        if (!newWindow)
        {
            var self = mux.Driver.CurrentPane;

            if (!self.IsNone)
            {
                await mux.Driver.KillPaneAsync(self).ConfigureAwait(false);
            }
        }

        return 0;
    }

    private static async Task<int> OpenSessionAsync(IMuxDriver mux, WindowSession session, IFleetLog log)
    {
        if (!SwitchProjectHandler.Applies(mux))
        {
            return Fail("sessions need the built-in multiplexer");
        }

        var saved = Adapters.Projects();
        var local = new List<string>();

        foreach (var wanted in session.Projects.Where(p => !p.IsRemote))
        {
            if (saved.Load(wanted.Name) is not { } project)
            {
                Console.Error.WriteLine($"fleet: {wanted.Name} is no longer a project; the session opens without it.");
                continue;
            }

            if (await ProjectOpener.EnsureOpenAsync(mux, project).ConfigureAwait(false) is { } failed)
            {
                return Fail(failed);
            }

            local.Add(project.Name);
        }

        await mux.ListWorkspacesAsync().ConfigureAwait(false);

        var hosts = session.Projects.Where(p => p.IsRemote).Select(p => p.Host!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (hosts.Count > 0)
        {
            using IApplication app = FleetUi.Start();
            foreach (var failed in ManageRemotesView.ConnectAll(app, Adapters.Remotes(), Adapters.KnownRemotes(), hosts))
            {
                Console.Error.WriteLine($"fleet: {failed}");
            }
        }

        log.Write($"session {session.Name}: {local.Count} local, {hosts.Count} remote");
        return await EmbeddedWiring.AttachAsync(local.FirstOrDefault(), null, log, session).ConfigureAwait(false);
    }

    private static async Task<int> OpenAsWorkspaceAsync(IMuxDriver mux, Project chosen, IFleetLog log)
    {
        var located = await new LocateProjectHandler(mux).HandleAsync([chosen]).ConfigureAwait(false);

        if (!located.TryGetValue(chosen.Name, out var where) || !where.Open)
        {
            var opened = await new OpenProjectHandler(mux)
                .HandleAsync(new OpenProjectCommand(
                    chosen, AgentHarness.Orchestrator, Adapters.Executable, null, Adapters.MainOrchestratorInNvim(chosen.Name)))
                .ConfigureAwait(false);

            if (!opened.Succeeded)
            {
                return Fail(opened.Error!);
            }

            var runnable = new ListAgentsHandler(Adapters.Agents()).Handle(chosen.Name)
                .Where(a => Adapters.OnPath(
                    AgentHarness.CommandFor(a.Harness, orchestratorInNvim: Adapters.SubOrchestratorsInNvim(chosen.Name))[0]))
                .ToList();

            await new RestoreSessionHandler(mux, Adapters.SubOrchestratorsInNvim(chosen.Name), Adapters.Agents())
                .HandleAsync(chosen.Name, chosen.Root, runnable)
                .ConfigureAwait(false);

            await mux.FocusPaneAsync(opened.Value.DashPane).ConfigureAwait(false);
        }

        if (EmbeddedWiring.InsideClient)
        {
            var shown = await new SwitchProjectHandler(mux).HandleAsync(chosen.Name).ConfigureAwait(false);
            return shown.Succeeded ? 0 : Fail(shown.Error!);
        }

        return await EmbeddedWiring.AttachAsync(chosen.Name, null, log).ConfigureAwait(false);
    }

    private static async Task<(string? WindowId, bool NewWindow)?> ResolveWindowAsync(
        IMuxDriver mux)
    {
        var currentWindow = Adapters.CurrentWindow(mux);
        var panes = await mux.ListPanesAsync().ConfigureAwait(false);
        var windowPanes = panes.Where(p => p.WindowId == currentWindow).ToList();
        var projects = Adapters.Projects().List();

        var alreadyFleetWindow = windowPanes.Any(
            p => projects.Any(project => PathKey.Same(p.Cwd, project.Root)));

        if (alreadyFleetWindow || windowPanes.Count <= 1)
        {
            return (currentWindow, false);
        }

        var choice = PromptTakeOver(windowPanes.Count - 1);

        if (choice == DialogChoice.Cancelled)
        {
            return null;
        }

        if (choice == DialogChoice.Secondary)
        {
            return (null, true);
        }

        var self = mux.CurrentPane;

        foreach (var pane in windowPanes.Where(p => p.Id != self))
        {
            await mux.KillPaneAsync(pane.Id).ConfigureAwait(false);
        }

        return (currentWindow, false);
    }

    private static DialogChoice PromptTakeOver(int otherPaneCount)
    {
        using IApplication app = FleetUi.Start();

        var plural = otherPaneCount == 1 ? "pane" : "panes";

        return FleetDialog.Choose(
            app,
            "Open fleet here?",
            [
                $"This window has {otherPaneCount} other {plural} open.",
                "Opening fleet here closes them.",
            ],
            "Continue",
            "New window");
    }

    private static ProjectPick? Choose()
    {
        var projects = Adapters.Projects();
        var keymaps = Adapters.Keymaps();
        var creator = new CreateProjectHandler(projects);
        var remover = new RemoveProjectHandler(projects);
        var sessions = Adapters.Sessions();

        var driver = Adapters.Mux(Adapters.Log()).Driver;

        Func<string, string?>? folders = null;

        if (Adapters.OnPath(FileBrowser.Command))
        {
            folders = wanted => Adapters.PickFolder(
                driver,
                "fleet",
                FileBrowser.StartIn(wanted, Directory.Exists, Adapters.HomeDirectory));
        }

        using IApplication app = FleetUi.Start();

        var keymap = new Keymap(keymaps.Load());

        return PickProjectView.Show(
            app,
            keymap,
            new PickProjectHandler(projects),
            new PickProjectCallbacks(
                CreateProject: () => CreateProjectView.Show(app, creator, folders),
                RemoveProject: project =>
                {
                    var confirmed = FleetDialog.Confirm(
                        app,
                        $"Remove {project.Name}?",
                        [
                            "fleet forgets this project.",
                            $"{project.Root} and everything in it stays on disk.",
                        ],
                        "Remove");

                    if (!confirmed)
                    {
                        return null;
                    }

                    var dropped = remover.Handle(project);

                    return dropped.Succeeded ? dropped.Value : dropped.Error;
                },
                ShowMenu: () => FleetUi.Menu(app, keymap, MenuActions),
                EditKeybinds: () => EditKeybindsView.Show(app, keymaps, keymap),
                Sessions: SwitchProjectHandler.Applies(driver) && !EmbeddedWiring.InsideClient ? sessions.List : null,
                RemoveSession: session =>
                    FleetDialog.Confirm(app, $"Remove session {session.Name}?", ["Its projects stay; only the saved set is forgotten."], "Remove")
                        ? (sessions.Remove(session.Name) ? $"removed session {session.Name}" : null)
                        : null));
    }

    private static int Fail(string reason)
    {
        Console.Error.WriteLine($"fleet: {reason}");
        return 1;
    }
}
