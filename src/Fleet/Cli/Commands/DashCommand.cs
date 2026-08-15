using Fleet.Cli.Composition;
using Fleet.Cli.Models;
using Fleet.Features.Dashboard.ShowDashboard;
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

        var keymaps = Adapters.Keymaps();
        var git = Adapters.Git();
        var log = Adapters.Log();
        var mux = Adapters.Mux(log);

        Adapters.MarkDashboardPane(project.Name);

        var approvals = Adapters.ApprovalInbox();

        using IApplication app = FleetUi.Start();

        var keymap = new Keymap(keymaps.Load());

        try
        {
            ShowDashboardView.Show(
                app,
                project.Name,
                keymap,
                DashboardWiring.For(
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
                    log));
        }
        finally
        {
            approvals.Retire(project.Name);
        }

        return 0;
    }
}
