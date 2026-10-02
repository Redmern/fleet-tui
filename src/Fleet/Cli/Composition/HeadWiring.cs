using System.Diagnostics;
using Fleet.Features.Head.ServeHead;
using Fleet.Features.Head.ServeHead.Models;
using Fleet.Features.Projects.LocateProject;
using Fleet.Platform.Claude;
using Fleet.Platform.Mcp;
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

        var service = new HeadService(Deps(mux, log));

        var serving = new McpServing(McpServerId.ServerName, ToolInfos(), service.HandleAsync);

        await Adapters.McpServer(log).RunAsync(serving, ct).ConfigureAwait(false);

        return 0;
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
            log);

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

        var started = DateTime.UtcNow;
        var exit = RunClaude(folder, HeadLaunch.ClaudeArgs(settings, resume));

        if (resume && exit != 0 && DateTime.UtcNow - started < QuickExit)
        {
            exit = RunClaude(folder, HeadLaunch.ClaudeArgs(settings, resume: false));
        }

        return exit;
    }

    private static int RunClaude(string folder, IReadOnlyList<string> args)
    {
        var psi = new ProcessStartInfo(AgentHarness.Claude)
        {
            UseShellExecute = false,
            WorkingDirectory = folder,
        };

        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
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
