using Fleet.Cli.Composition;
using Fleet.Features.Agents.NewAgent;
using Fleet.Features.Agents.NewAgent.Models;
using Fleet.Features.Mcp.SyncClaudeConfig;
using Fleet.Features.Orchestrations.Dispatch.Models;
using Fleet.Features.Repositories.AddRepository;
using Fleet.Features.Repositories.AddRepository.Models;
using Fleet.Platform.Claude;
using Fleet.Platform.Codex;
using Fleet.Platform.Git;
using Fleet.Platform.Harness;
using Fleet.Platform.Opencode;
using Fleet.Platform.Pi;
using Fleet.Platform.Storage;
using Fleet.Ports;
using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;
using Fleet.Ports.Harness;
using Fleet.Ports.Mcp.Models;
using Fleet.Shared.Harness;
using Fleet.Shared.Mcp;
using Fleet.Shared.Results;
using Fleet.Shared.Settings;
using Fleet.Shared.Settings.Enums;
using Fleet.Shared.Settings.Models;
using Fleet.Shared.Status;
using Fleet.Shared.Status.Enums;
using Fleet.Shared.Status.Models;

namespace Fleet.Tests.EndToEnd.MixedTeam;

// One fleet project wired like the composition root, with a fake mux, fake harnesses and a fake clock.
//
// Real: McpActions (the object `fleet mcp --caller x` serves; one per caller, as in McpWiring) for dispatch,
// new_agent, tell_agent, report, wait_for_agent, list_agents and list_subs, with the real settings store, inbox store
// and notifier under a temp config home (FleetHome.Scope); the opencode/codex/pi config writers; NewAgentHandler on a real git
// repository; PaneActivity + OwnerNotices (the dashboard's idle watch).
//
// Passed in where McpWiring passes the real thing (the McpActions seams):
// - the clock: a SkippingClock, so the Enter delay, the echo polls and the quiet window take no real time;
// - the slug namer: McpWiring's asks the claude CLI; this one answers what the test sets, or nothing;
// - the config writers: claude's write nothing (the real ones edit ~/.claude.json), and the codex and pi writers
//   keep their user-level files (~/.codex/config.toml, ~/.pi/agent/trust.json) under the project root;
// - the folder-trust writer: it only records the folder.
//
// Mirrored here: a claude worker. StartClaudeWorkerAsync runs the same NewAgentHandler as McpActions.Start and
// stores the owner, with the handler's own defaults instead of the project settings.
// Not covered: the McpDispatcher permission gate and the JSON-RPC transport in front of McpActions.
internal sealed class MixedTeamRig
{
    private readonly GitRunner _git = new();
    private readonly List<string> _trusted = [];

    public MixedTeamRig(string root)
    {
        Root = root;
        Directory.CreateDirectory(root);
        Mux = new FakeHarnessMux(Clock);
        ClaudeConfigs = new RecordingHarnessConfigs(
            new HarnessConfigs(new Dictionary<string, IHarnessConfig>(StringComparer.Ordinal), new NullHarnessConfig()));
        OtherConfigs = new RecordingHarnessConfigs(Configs());
        SubOrchestratorsInNvim(false);
    }

    public void SubOrchestratorsInNvim(bool on) =>
        Adapters.Settings().Save(Project, SettingsConfig.Default.WithSubOrchestratorsInNvim(on));

    public string Project { get; } = $"mixed-{Guid.NewGuid():N}"[..14];

    public string Root { get; }

    public SkippingClock Clock { get; } = new();

    public FakeHarnessMux Mux { get; }

    public RecordingSlugNamer Namer { get; } = new();

    public RecordingHarnessConfigs ClaudeConfigs { get; }

    public RecordingHarnessConfigs OtherConfigs { get; }

    public IReadOnlyList<string> TrustedFolders
    {
        get
        {
            lock (_trusted)
            {
                return [.. _trusted];
            }
        }
    }

    public MemoryStore Store { get; } = new();

    public RecordingLog Log { get; } = new();

    public string CodexUserConfig => Path.Combine(Root, "home", ".codex", "config.toml");

    public string PiTrustStore => Path.Combine(Root, "home", ".pi", "agent", "trust.json");

    public IReadOnlyList<string> DesktopNotifications =>
        File.Exists(FileNotifyStore.File) ? File.ReadAllLines(FileNotifyStore.File) : [];

    public AgentRecord Record(string repository, string branch) =>
        Store.List(Project).Single(a => a.Repository == repository && a.Branch == branch);

    public async Task<string> AddRepositoryAsync(string name)
    {
        var created = await new AddRepositoryHandler(_git)
            .HandleAsync(AddRepositoryCommand.CreateNew(Root, name, "main"));

        Assert.True(created.Succeeded, created.Error);

        return created.Value!.Path;
    }

    public McpActions Actions(string caller) => new(
        Project,
        Root,
        caller,
        _git,
        Mux,
        Store,
        ClaudeConfigs,
        OtherConfigs,
        Namer,
        Clock,
        Trust,
        Log);

    public Task<McpResult> CallAsync(string caller, HarnessTool tool, params (string Key, string Value)[] arguments) =>
        Actions(caller).PerformAsync(
            new McpRequest(HarnessToolIds.For(tool), arguments.ToDictionary(a => a.Key, a => a.Value)),
            CancellationToken.None);

    public async Task<DispatchReply> DispatchAsync(string caller, string message, string harness)
    {
        var before = Store.List(Project).Select(a => a.Worktree).ToHashSet();

        var reply = await CallAsync(caller, HarnessTool.Dispatch, ("message", message), ("harness", harness));

        Assert.False(reply.IsError, reply.Text);

        var sub = Store.List(Project).Single(a => !before.Contains(a.Worktree));

        return new DispatchReply(sub.Branch, sub.Worktree, reply.Text);
    }

    public async Task<AgentRecord> StartClaudeWorkerAsync(string owner, string repository, string directory, string branch)
    {
        var created = await new NewAgentHandler(_git, Mux, Store)
            .HandleAsync(new NewAgentCommand(Project, repository, directory, branch, "main", HarnessRegistry.ClaudeName, owner));

        Assert.True(created.Succeeded, created.Error);
        Store.Save(Project, created.Value! with { Owner = owner });

        return Record(repository, branch);
    }

    // What claude's status hook does for a running session: it leaves the session's cross-session inbox address.
    public Task ClaudeHookReportsInboxAsync(AgentRecord agent, string address) =>
        Adapters.AgentStates().ReportAsync(
            new AgentReport(agent.Worktree, "session-1", AgentState.Idle, DateTime.UtcNow, Inbox: address));

    // The dashboard's status refresh (DashboardWiring: new PaneActivity(mux, new OwnerNotices(...).WentIdle)).
    public PaneActivity Dashboard() =>
        new(Mux, new OwnerNotices(Project, Root, Mux, Store, Log, () => false, Clock).WentIdle, Clock);

    public async Task<IReadOnlyList<AgentRecord>> RefreshAsync(PaneActivity dashboard) =>
        dashboard.For(Store.List(Project), await Mux.ListPanesAsync(), _ => null);

    public static async Task UntilAsync(Func<bool> met, string what)
    {
        for (var i = 0; i < 250 && !met(); i++)
        {
            await Task.Delay(20);
        }

        Assert.True(met(), what);
    }

    private void Trust(string folder)
    {
        lock (_trusted)
        {
            _trusted.Add(folder);
        }
    }

    private HarnessConfigs Configs()
    {
        var skills = FleetSkills.Embedded();

        return new HarnessConfigs(
            new Dictionary<string, IHarnessConfig>(StringComparer.Ordinal)
            {
                [HarnessRegistry.OpencodeName] = new OpencodeHarnessConfig(Server, skills, NoExcludes),
                [HarnessRegistry.CodexName] = new CodexHarnessConfig(Server, skills, NoExcludes, CodexUserConfig),
                [HarnessRegistry.PiName] = new PiHarnessConfig(Server, skills, NoExcludes, PiTrustStore),
            },
            new NullHarnessConfig());
    }

    private static Fleet.Ports.Claude.Models.McpServerEntry Server(string project, string caller) =>
        McpRegistration.For(Adapters.Executable, project, caller);

    private static void NoExcludes(string folder, IReadOnlyList<string> patterns)
    {
    }

    internal sealed class MemoryStore : IAgentStore
    {
        private readonly object _gate = new();
        private readonly List<AgentRecord> _agents = [];

        public void Save(string project, AgentRecord agent)
        {
            lock (_gate)
            {
                _agents.RemoveAll(a => a.Worktree == agent.Worktree);
                _agents.Add(agent);
            }
        }

        public IReadOnlyList<AgentRecord> List(string project)
        {
            lock (_gate)
            {
                return [.. _agents];
            }
        }

        public bool Replace(string project, AgentRecord agent)
        {
            lock (_gate)
            {
                var at = _agents.FindIndex(a => a.Worktree == agent.Worktree);

                if (at < 0)
                {
                    return false;
                }

                _agents[at] = agent;
                return true;
            }
        }

        public bool Remove(string project, string worktree)
        {
            lock (_gate)
            {
                return _agents.RemoveAll(a => a.Worktree == worktree) > 0;
            }
        }
    }

    internal sealed class RecordingLog : IFleetLog
    {
        private readonly List<string> _lines = [];

        public void Swallowed(Exception e) => Write(e.Message);

        public void Write(string line)
        {
            lock (_lines)
            {
                _lines.Add(line);
            }
        }

        public IReadOnlyList<string> Tail(int lines)
        {
            lock (_lines)
            {
                return [.. _lines.TakeLast(lines)];
            }
        }
    }
}
