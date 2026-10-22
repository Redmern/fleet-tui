using System.Text.Json;
using Fleet.Cli.Composition;
using Fleet.Features.Projects.RestoreSession;
using Fleet.Platform.Mux.Fake;
using Fleet.Platform.Mux.Embedded.Daemon;
using Fleet.Platform.Mux.Embedded.Model;
using Fleet.Platform.Mux.Embedded.Protocol;
using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;
using Fleet.Ports.Projects;
using Fleet.Ports.Projects.Models;
using Fleet.Shared.Constants;
using Fleet.Shared.Orchestrations;

namespace Fleet.Tests.Platform.Mux.Embedded;

public sealed class RestoreSkipsRemovedSubsTests
{
    private static readonly string Project = Path.Combine(Path.GetTempPath(), "techweb");

    private static readonly string Removed = OrchestrationPaths.For(Project, "gone");

    private static readonly string Kept = OrchestrationPaths.For(Project, "kept");

    private static IReadOnlyList<string> Same(IReadOnlyList<string> args) => args;

    private static bool OnlyKept(string cwd) => cwd == Kept;

    private static LayoutSnapshot Leaf(string cwd, string program = "claude") =>
        new() { Pane = new PaneSnapshot { Cwd = cwd, Args = [program] } };

    private static LayoutSnapshot Split(LayoutSnapshot first, LayoutSnapshot second) =>
        new() { SideBySide = true, Ratio = 0.5, First = first, Second = second };

    [Theory]
    [InlineData("/home/u/techweb/.fleet/orchestrations/fix-login", true)]
    [InlineData("/home/u/techweb/.fleet/orchestrations/fix-login/", true)]
    [InlineData(@"C:\Users\u\techweb\.fleet\orchestrations\fix-login", true)]
    [InlineData("C:/Users/u/techweb/.Fleet/Orchestrations/fix-login", true)]
    [InlineData("/home/u/techweb/.fleet/orchestrations", false)]
    [InlineData("/home/u/techweb/.fleet/orchestrations/fix-login/reports", false)]
    [InlineData("/home/u/techweb/backend/main", false)]
    [InlineData("/home/u/techweb", false)]
    [InlineData("", false)]
    public void A_pane_is_a_sub_orchestrator_when_its_cwd_is_an_orchestration_folder(string cwd, bool expected) =>
        Assert.Equal(expected, SavedSubOrchestrators.IsSubOrchestrator(cwd));

    [Fact]
    public void A_folder_from_OrchestrationPaths_is_a_sub_orchestrator() =>
        Assert.True(SavedSubOrchestrators.IsSubOrchestrator(OrchestrationPaths.For(Project, "fix-login")));

    [Fact]
    public void Dropping_leaves_other_panes_and_known_subs_and_an_emptied_tab_does_not_crash_restore()
    {
        var snapshot = new SessionSnapshot
        {
            Workspaces =
            [
                new WorkspaceSnapshot
                {
                    Name = "techweb",
                    ActiveTab = 2,
                    Tabs =
                    [
                        new TabSnapshot { Root = Split(Leaf(Project, "fleet"), Leaf(Removed)), ActivePane = 1 },
                        new TabSnapshot { Root = Leaf(Removed) },
                        new TabSnapshot { Root = Split(Leaf(Kept), Leaf(Removed)), ActivePane = 1, Zoomed = 1 },
                    ],
                    Floats = [new FloatSnapshot { Pane = new PaneSnapshot { Cwd = Removed, Args = ["claude"] }, Width = 40, Height = 10 }],
                    FloatsShown = true,
                    FloatFocused = true,
                },
                new WorkspaceSnapshot { Name = "hidden", Tabs = [new TabSnapshot { Root = Leaf(Removed) }] },
            ],
        };

        var dropped = SavedSubOrchestrators.DropUnknown(snapshot, OnlyKept);

        Assert.Equal(5, dropped.Count);
        Assert.All(dropped, cwd => Assert.Equal(Removed, cwd));

        var model = new MuxModel();
        var panes = model.Restore(snapshot, Same);

        Assert.Equal([Project, Kept], panes.Select(p => p.Cwd).Order(StringComparer.Ordinal));
        var workspace = Assert.Single(model.ListWorkspaces(null));
        Assert.Equal("techweb", workspace.Name);
        var techweb = model.Workspace("techweb")!;
        Assert.Equal(2, techweb.Tabs.Count);
        Assert.Empty(techweb.Floats);
        Assert.False(techweb.FloatsShown);
        Assert.All(techweb.Tabs, t => Assert.IsType<LayoutLeaf>(t.Root));
    }

    [Fact]
    public void Nothing_is_dropped_when_every_sub_has_a_record()
    {
        var snapshot = new SessionSnapshot
        {
            Workspaces = [new WorkspaceSnapshot { Name = "techweb", Tabs = [new TabSnapshot { Root = Split(Leaf(Removed), Leaf(Kept)) }] }],
        };

        Assert.Empty(SavedSubOrchestrators.DropUnknown(snapshot, _ => true));
        Assert.Equal(2, new MuxModel().Restore(snapshot, Same).Count);
    }

    [Fact]
    public async Task A_restarted_daemon_relaunches_a_sub_with_a_record_and_drops_one_without()
    {
        var dir = Directory.CreateTempSubdirectory("fleet-restore-subs-").FullName;
        var file = Path.Combine(dir, "embedded-session.json");
        var snapshot = new SessionSnapshot
        {
            Workspaces =
            [
                new WorkspaceSnapshot
                {
                    Name = "techweb",
                    Tabs =
                    [
                        new TabSnapshot { Root = Split(Leaf(Project, "pwsh"), Leaf(Kept)) },
                        new TabSnapshot { Root = Leaf(Removed) },
                    ],
                },
            ],
        };
        File.WriteAllText(file, JsonSerializer.Serialize(snapshot, SessionJsonContext.Default.SessionSnapshot));

        var logged = new List<string>();
        var panes = new FakePanes();
        var name = $"fleet-test-{Guid.NewGuid():N}"[..20];
        var endpoint = new Endpoint(OperatingSystem.IsWindows() ? name : Path.Combine(Path.GetTempPath(), name + ".sock"));
        var daemon = new FleetDaemon(new DaemonOptions
        {
            Endpoint = endpoint,
            Pty = panes.NewPty,
            Terminal = panes.NewTerminal,
            SessionFile = file,
            Log = line => { lock (logged) { logged.Add(line); } },
            IsKnownAgent = OnlyKept,
        });

        using var stop = new CancellationTokenSource();
        var running = daemon.RunAsync(stop.Token);

        try
        {
            await using (var control = await DaemonTests.TestClient.ConnectAsync(endpoint, ClientRoles.Control, 0, 0, null))
            {
                var listed = await control.RequestAsync(new ControlRequest { Op = "list-panes" });
                Assert.Equal(2, listed.Panes!.Count);
            }

            Assert.Equal([Project, Kept], panes.Started.Select(p => p.Cwd).Order(StringComparer.Ordinal));
            Assert.Equal(["--continue"], panes.ByProgram("claude")!.Args);
            lock (logged)
            {
                Assert.Single(logged, l => l.Contains(Removed, StringComparison.Ordinal));
            }
        }
        finally
        {
            await stop.CancelAsync();
            await running;
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void The_wiring_knows_a_sub_only_while_some_project_still_has_its_record()
    {
        var projects = new Projects(new Project("api", "/p/api"), new Project("techweb", Project));
        var agents = new Agents(("techweb", Kept));

        Assert.True(EmbeddedWiring.HasAgentRecord(projects, agents, Kept));
        Assert.True(EmbeddedWiring.HasAgentRecord(projects, agents, Kept + Path.DirectorySeparatorChar));
        Assert.False(EmbeddedWiring.HasAgentRecord(projects, agents, Removed));
        Assert.False(EmbeddedWiring.HasAgentRecord(new Projects(new Project("api", "/p/api")), agents, Kept));
    }

    [Fact]
    public async Task Opening_a_project_respawns_only_sub_folders_that_still_have_a_record()
    {
        var root = Directory.CreateTempSubdirectory("fleet-restore-subs-").FullName;

        try
        {
            var kept = OrchestrationPaths.For(root, "kept");
            var removed = OrchestrationPaths.For(root, "gone");
            Directory.CreateDirectory(kept);
            Directory.CreateDirectory(removed);
            var record = new AgentRecord(kept, "techweb", "sub-kept", AgentHarness.Orchestrator, "", false, Hidden: true, Open: true);
            var mux = new FakeMuxDriver();

            var restored = await new RestoreSessionHandler(mux).HandleAsync("techweb", root, [record]);

            Assert.Equal(1, restored);
            var pane = Assert.Single(await mux.ListPanesAsync());
            Assert.Equal(kept, pane.Cwd);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class Projects(params Project[] projects) : IProjectStore
    {
        public Project? Load(string name) => projects.FirstOrDefault(p => p.Name == name);

        public IReadOnlyList<Project> List() => projects;

        public void Save(Project project)
        {
        }

        public void Remove(string name)
        {
        }
    }

    private sealed class Agents(params (string Project, string Worktree)[] records) : IAgentStore
    {
        public void Save(string project, AgentRecord agent)
        {
        }

        public bool Replace(string project, AgentRecord agent) => false;

        public IReadOnlyList<AgentRecord> List(string project) =>
        [
            .. records.Where(r => r.Project == project)
                .Select(r => new AgentRecord(r.Worktree, "techweb", "sub", AgentHarness.Orchestrator, "", false)),
        ];

        public bool Remove(string project, string worktree) => false;
    }
}
