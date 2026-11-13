using System.Globalization;
using Fleet.Cli.Composition;
using Fleet.Features.Agents.NewAgent;
using Fleet.Features.Agents.NewAgent.Models;
using Fleet.Features.Mcp.SyncClaudeConfig;
using Fleet.Features.Orchestrations.Dispatch;
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

// One fleet project wired like the composition root, with a fake mux and fake harnesses.
//
// Real: McpActions (the object `fleet mcp --caller x` serves; one per caller, as in McpWiring) for new_agent,
// tell_agent, report, wait_for_agent, list_agents and list_subs, with the real settings store, inbox store and
// notifier under a temp FLEET_CONFIG_HOME; DispatchHandler; the opencode/codex/pi config writers; NewAgentHandler on
// a real git repository; PaneActivity + OwnerNotices (the dashboard's idle watch).
//
// Mirrored here because the production wiring cannot run in a test:
// - dispatch: McpActions.Dispatch names the slug with the claude CLI (Adapters.SlugNamer) and its codex/pi writers
//   edit ~/.codex and ~/.pi. DispatchAsync builds the same DispatchHandler without a namer and with those two
//   writers pointed at temp files.
// - a claude worker: McpActions.Start trusts the folder in ~/.claude.json. StartClaudeWorkerAsync runs the same
//   NewAgentHandler and stores the owner, without the claude config.
// Not covered: the McpDispatcher permission gate and the JSON-RPC transport in front of McpActions.
internal sealed class MixedTeamRig
{
    private readonly GitRunner _git = new();

    public MixedTeamRig(string root)
    {
        Root = root;
        Directory.CreateDirectory(root);
        SubOrchestratorsInNvim(false);
    }

    public void SubOrchestratorsInNvim(bool on) =>
        Adapters.Settings().Save(Project, SettingsConfig.Default.WithSubOrchestratorsInNvim(on));

    public string Project { get; } = $"mixed-{Guid.NewGuid():N}"[..14];

    public string Root { get; }

    public FakeHarnessMux Mux { get; } = new();

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

    public async Task<McpResult> CallAsync(string caller, HarnessTool tool, params (string Key, string Value)[] arguments)
    {
        var actions = new McpActions(
            Project,
            Root,
            caller,
            _git,
            Mux,
            Store,
            HarnessRegistration.ClaudeConfigs(),
            HarnessRegistration.NonClaudeConfigs(),
            Adapters.SlugNamer(),
            TimeProvider.System,
            ClaudeWiring.TrustFolder,
            Log);

        return await actions.PerformAsync(
            new McpRequest(HarnessToolIds.For(tool), arguments.ToDictionary(a => a.Key, a => a.Value)),
            CancellationToken.None);
    }

    public async Task<DispatchReply> DispatchAsync(string caller, string message, string harness)
    {
        var dispatcher = new DispatchHandler(
            Mux, Store, new NullHarnessConfig(), TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(20),
            settings: Adapters.Settings(), intents: Adapters.Intents(), log: Log, configs: Configs());

        var reply = await dispatcher.HandleAsync(
            new DispatchCommand(Project, Root, message, caller, Harness: harness),
            DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));

        Assert.True(reply.Succeeded, reply.Error);

        return reply.Value!;
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
        new(Mux, new OwnerNotices(Project, Root, Mux, Store, Log, () => false).WentIdle);

    public async Task<IReadOnlyList<AgentRecord>> RefreshAsync(PaneActivity dashboard) =>
        dashboard.For(Store.List(Project), await Mux.ListPanesAsync(), _ => null);

    // There is no clock seam in PaneActivity or McpActions, so the quiet window is passed by making the pane mark
    // fleet wrote older, as if the same screen had been seen that long ago.
    public static void PaneStaysQuietFor(AgentRecord agent, TimeSpan span)
    {
        var mark = Path.Combine(agent.Worktree, ".fleet", PaneQuiet.FileName);
        var parts = File.ReadAllText(mark).Trim().Split(' ');
        var since = DateTime.Parse(parts[1], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

        File.WriteAllText(mark, $"{parts[0]} {(since - span).ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)}");
    }

    public static async Task UntilAsync(Func<bool> met, string what)
    {
        for (var i = 0; i < 250 && !met(); i++)
        {
            await Task.Delay(20);
        }

        Assert.True(met(), what);
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
