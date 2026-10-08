using Fleet.Cli.Composition;
using Fleet.Cli.Models;
using Fleet.Features.Dashboard.ShowDashboard;
using Fleet.Ports.Projects.Models;
using Fleet.Shared;
using Fleet.Ui;
using Terminal.Gui.App;

namespace Fleet.Cli.Commands;

public static class DashCommand
{
    public static int Run(Invocation invocation)
    {
        if (invocation.Project is null)
        {
            Console.Error.WriteLine("fleet dash: --project <name> is required");
            return 2;
        }

        var project = Adapters.Projects().Load(invocation.Project);

        if (project is null)
        {
            Console.Error.WriteLine($"fleet dash: no saved project '{invocation.Project}'");
            return 1;
        }

        var build = new FreshBuild(Adapters.Executable);

        if (!Show(project, build))
        {
            return 0;
        }

        Adapters.Log().Write(LogTag.For(project.Name, "fleet was updated; the dashboard restarts on the new build"));
        return build.RunAgain(["dash", "--project", project.Name]);
    }

    private static bool Show(Project project, FreshBuild build)
    {
        var restart = false;
        var keymaps = Adapters.Keymaps();
        var git = Adapters.Git();
        var log = Adapters.Log();
        var mux = Adapters.Mux(log);

        var syncing = Task.Run(() =>
        {
            try
            {
                var synced = ClaudeWiring.SyncRoot(project.Name, project.Root);

                if (!synced.Succeeded)
                {
                    log.Write(LogTag.For(project.Name, $"claude config: {synced.Error}"));
                }
            }
            catch (Exception e)
            {
                log.Write(LogTag.For(project.Name, $"claude config: {e.Message}"));
            }
        });

        var approvals = Adapters.ApprovalInbox();

        using IApplication app = FleetUi.Start();

        var keymap = new Keymap(keymaps.Load());

        var drift = Adapters.SetupDrift(keymap);

        if (drift is not null)
        {
            log.Write(LogTag.For(project.Name, drift));
        }

        try
        {
            ShowDashboardView.Show(
                app,
                project.Name,
                keymap,
                notice: drift,
                callbacks: DashboardWiring.For(
                    app,
                    project,
                    keymap,
                    keymaps,
                    git,
                    mux.Driver,
                    Adapters.Agents(),
                    Adapters.Requests(),
                    Adapters.Workspaces(),
                    Adapters.Settings(),
                    Adapters.SettingsSync(),
                    approvals,
                    log) with
                {
                    Outdated = () => restart = build.ReplacedAndSettled,
                });
        }
        finally
        {
            approvals.Retire(project.Name);
            syncing.GetAwaiter().GetResult();
        }

        return restart;
    }
}
