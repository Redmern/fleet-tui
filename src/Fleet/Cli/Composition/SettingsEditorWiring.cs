using Fleet.Features.Menu.EditSettings;
using Fleet.Platform.Storage;
using Fleet.Ports.Mux;
using Fleet.Ports.Mux.Models;
using Fleet.Ports.Projects.Models;
using Fleet.Shared.Constants;
using Fleet.Shared.Settings.Models;

namespace Fleet.Cli.Composition;

public static class SettingsEditorWiring
{
    private const string PaneTitle = "fleet settings";

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    public static OpenSettingsInEditor For(Project project, Func<SettingsConfig, string?> resync)
    {
        var store = new JsonSettingsStore();

        return new OpenSettingsInEditor(
            () => store.Exists(project.Name),
            next => store.Save(project.Name, next),
            exited => Spawn(project, exited),
            () => store.Reload(project.Name),
            resync);
    }

    private static string? Spawn(Project project, Action exited)
    {
        if (JsonSettingsStore.FileFor(project.Name) is not { } file)
        {
            return $"'{project.Name}' has no settings file to open.";
        }

        var selection = Adapters.Mux(Adapters.Log());

        if (selection.Unsupported is not null)
        {
            return selection.Unsupported;
        }

        var mux = selection.Driver;
        var pane = Adapters.SpawnHereAsync(
            mux,
            new SpawnOptions
            {
                Cwd = project.Root,
                SessionName = project.Name,
                WindowId = Adapters.CurrentWindow(mux),
                Args = AgentHarness.BrowseCommandFor(PaneTitle, file),
            }).GetAwaiter().GetResult();

        if (pane.IsNone)
        {
            return $"the {mux.Name} multiplexer did not respond. Run 'fleet doctor'.";
        }

        mux.SetTitleAsync(pane, PaneTitle).GetAwaiter().GetResult();
        mux.FocusPaneAsync(pane).GetAwaiter().GetResult();

        _ = Task.Run(() => WaitForExitAsync(mux, pane, exited));

        return null;
    }

    private static async Task WaitForExitAsync(IMuxDriver mux, PaneId pane, Action exited)
    {
        while ((await mux.ListPanesAsync().ConfigureAwait(false)).Any(p => p.Id == pane))
        {
            await Task.Delay(PollInterval).ConfigureAwait(false);
        }

        exited();
    }
}
