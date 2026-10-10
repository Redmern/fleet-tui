using System.Diagnostics;
using Fleet.Features.Head.ServeHead;
using Fleet.Features.Head.ServeHead.Models;
using Fleet.Features.Projects.LocateProject;
using Fleet.Platform.Claude;
using Fleet.Platform.Mcp;
using Fleet.Platform.Mux.Embedded.Pty;
using Fleet.Platform.Storage;
using Fleet.Ports.Claude.Models;
using Fleet.Ports.Mcp.Models;
using Fleet.Ports.Mux;
using Fleet.Ports.Projects.Models;
using Fleet.Shared.Constants;
using Fleet.Shared.Mcp;

namespace Fleet.Cli.Composition;

public static class HeadWiring
{
    private static readonly TimeSpan QuickExit = TimeSpan.FromSeconds(5);

    public static string Folder => Path.Combine(Adapters.ConfigDirectory, "head");

    public static async Task<int> RunMcpAsync(CancellationToken ct)
    {
        var log = Adapters.Log();
        var mux = Adapters.Mux(log).Driver;

        var service = new HeadService(Deps(mux, log) with { Inboxes = Adapters.AgentInboxes() });

        var serving = new McpServing(McpServerId.ServerName, ToolInfos(), service.HandleAsync);

        await Adapters.McpServer(log).RunAsync(serving, ct).ConfigureAwait(false);

        return 0;
    }

    public static Func<string, IReadOnlyDictionary<string, string>, CancellationToken, Task<(string Text, bool Failed)>>
        ServeOrigin(Func<IMuxDriver> mux, Ports.IFleetLog log)
    {
        var service = new Lazy<HeadService>(() => new HeadService(Deps(mux(), log)));

        return async (tool, arguments, ct) =>
        {
            log.Write($"fleetd: the origin's head runs {tool} here");
            var result = await service.Value.ServeOriginAsync(new McpRequest(tool, arguments), ct).ConfigureAwait(false);
            return (result.Text, result.IsError);
        };
    }

    public static HeadDeps Deps(IMuxDriver mux, Ports.IFleetLog log) =>
        new(
            Adapters.Projects(),
            mux,
            Adapters.Settings(),
            Adapters.Approvals(),
            Adapters.Agents(),
            Adapters.Requests(),
            Adapters.Workspaces(),
            (project, ct) => IsOpenAsync(mux, project, ct),
            project => ProjectOpener.EnsureOpenAsync(mux, project),
            Adapters.DashPane,
            log,
            Adapters.Remotes(),
            Adapters.KnownRemotes(),
            (project, ct) => ProjectStructureReader.ReadAsync(Adapters.Git(), mux, Adapters.Agents(), project, ct),
            HeadPanes.SetVisible(mux, Adapters.Agents(), Adapters.SubOrchestratorsInNvim, ClaudeWiring.TrustFolder),
            Iso: Adapters.Iso());

    public static int Launch(bool voice)
    {
        var folder = Folder;
        var state = Path.Combine(folder, ".fleet");

        Directory.CreateDirectory(state);
        File.WriteAllText(Path.Combine(folder, HeadBrief.FileName), HeadBrief.Text);
        File.WriteAllText(Path.Combine(state, HeadLaunch.VoiceOnFile), HeadLaunch.VoiceOn);
        File.WriteAllText(Path.Combine(state, HeadLaunch.VoiceOffFile), HeadLaunch.VoiceOff);

        var server = new McpServerEntry(McpServerId.ServerName, Adapters.Executable, HeadLaunch.McpArgs);
        var allow = HeadTools.Names.Select(n => $"mcp__{McpServerId.ServerName}__{n}").ToList();

        var synced = new ClaudeConfigWriter().SyncWorktree(server, folder, allow, [], []);

        if (!synced.Succeeded)
        {
            Console.Error.WriteLine($"fleet head: {synced.Error}");
        }

        ClaudeWiring.TrustFolder(folder);

        var settings = Path.Combine(state, voice ? HeadLaunch.VoiceOnFile : HeadLaunch.VoiceOffFile);
        var marker = Path.Combine(state, HeadLaunch.StartedMarker);
        var resume = File.Exists(marker);

        File.WriteAllText(marker, string.Empty);

        var launch = ClaudeLaunch.Head(new JsonSettingsStore().LoadHead());
        var started = DateTime.UtcNow;
        var exit = RunClaude(folder, HeadLaunch.ClaudeArgs(settings, resume, launch));

        if (resume && exit != 0 && DateTime.UtcNow - started < QuickExit)
        {
            exit = RunClaude(folder, HeadLaunch.ClaudeArgs(settings, resume: false, launch));
        }

        return exit;
    }

    private static int RunClaude(string folder, IReadOnlyList<string> args)
    {
        var psi = new ProcessStartInfo
        {
            UseShellExecute = false,
            WorkingDirectory = folder,
        };

        if (OperatingSystem.IsWindows())
        {
            psi.FileName = HeadLaunch.WindowsShell;
            psi.Arguments = HeadLaunch.ShellArguments(AgentHarness.Claude, args, WindowsCommandLine.Quote);
        }
        else
        {
            psi.FileName = AgentHarness.Claude;

            foreach (var arg in args)
            {
                psi.ArgumentList.Add(arg);
            }
        }

        foreach (var (key, value) in AgentHarness.SessionPersistence)
        {
            psi.Environment[key] = value.Length == 0 ? null : value;
        }

        try
        {
            using var process = Process.Start(psi);

            if (process is null)
            {
                Console.Error.WriteLine("fleet head: could not start claude.");
                return 1;
            }

            process.WaitForExit();

            return process.ExitCode;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException)
        {
            Console.Error.WriteLine($"fleet head: could not start claude: {e.Message}");
            return 1;
        }
    }

    private static async Task<bool> IsOpenAsync(IMuxDriver mux, Project project, CancellationToken ct)
    {
        var located = await new LocateProjectHandler(mux).HandleAsync([project], ct).ConfigureAwait(false);

        return located.TryGetValue(project.Name, out var where) && where.Open;
    }

    private static IReadOnlyList<McpToolInfo> ToolInfos() =>
    [
        .. HeadTools.All.Select(spec => new McpToolInfo(
            spec.Name,
            spec.Description,
            ToolSchema.For([.. spec.Params.Select(p => new SchemaField(p.Name, p.Type, p.Description, p.Required))]))),
    ];
}
