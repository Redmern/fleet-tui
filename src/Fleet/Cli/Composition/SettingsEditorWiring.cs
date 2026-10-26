using Fleet.Features.Menu.EditSettings;
using Fleet.Platform.Storage;
using Fleet.Ports.Editors;
using Fleet.Ports.Mux.Models;
using Fleet.Ports.Projects.Models;
using Fleet.Shared.Constants;
using Fleet.Shared.Settings;
using Fleet.Shared.Settings.Enums;
using Fleet.Shared.Settings.Models;

namespace Fleet.Cli.Composition;

public static class SettingsEditorWiring
{
    private const string PaneTitle = "fleet settings";

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    private static readonly TimeSpan Patience = TimeSpan.FromMinutes(10);

    public static OpenSettingsInEditor For(Project project, Func<SettingsConfig, string?> resync)
    {
        var store = Adapters.Settings();
        var editor = Adapters.ResolvedEditor(project.Name);

        return new OpenSettingsInEditor(
            () => store.Exists(project.Name),
            next => store.Save(project.Name, next),
            SpawnFor(editor, exited => Spawn(project, exited), exited => EditInVscode(project, exited)),
            () => store.Reload(project.Name),
            resync,
            editor);
    }

    public static Func<Action, string?> SpawnFor(
        Editor editor, Func<Action, string?> nvim, Func<Action, string?> vscode) =>
        editor == Editor.Vscode ? vscode : nvim;

    public static string? OpenInVscode(IFileEditor code, string file, Action exited, Action<string> toast, bool windows)
    {
        var trouble = code.Available ? code.EditAndWait(file, exited) : EditorChoice.InstallHint(windows);

        if (trouble is not null)
        {
            toast(trouble);
        }

        return trouble;
    }

    private static string? EditInVscode(Project project, Action exited)
    {
        if (JsonSettingsStore.FileFor(project.Name) is not { } file)
        {
            return $"'{project.Name}' has no settings file to open.";
        }

        return OpenInVscode(
            Adapters.EditorLauncher(),
            file,
            exited,
            body => Adapters.Toast($"fleet · {project.Name}", body),
            OperatingSystem.IsWindows());
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

        _ = Task.Run(() => WaitForExitAsync(() => mux.ListPanesAsync(), pane, exited, PollInterval, Patience));

        return null;
    }

    public static async Task WaitForExitAsync(
        Func<Task<IReadOnlyList<Pane>>> panes, PaneId pane, Action exited, TimeSpan interval, TimeSpan patience)
    {
        var deadline = DateTime.UtcNow + patience;

        try
        {
            while (DateTime.UtcNow < deadline && (await panes().ConfigureAwait(false)).Any(p => p.Id == pane))
            {
                await Task.Delay(interval).ConfigureAwait(false);
            }
        }
        catch (Exception)
        {
        }

        exited();
    }
}
