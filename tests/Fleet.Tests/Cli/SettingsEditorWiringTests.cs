using Fleet.Cli.Composition;
using Fleet.Ports.Editors;
using Fleet.Ports.Mux.Models;
using Fleet.Shared.Settings;
using Fleet.Shared.Settings.Enums;

namespace Fleet.Tests.Cli;

public class SettingsEditorWiringTests
{
    private static readonly PaneId Nvim = new("nvim-1");

    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(1);

    private static Pane Listed(PaneId id) => new(id, "w1", "t1", "techweb", "fleet settings", "/repos/techweb", false);

    [Fact]
    public async Task The_exit_fires_once_the_nvim_pane_is_gone()
    {
        var polls = 0;
        var exits = 0;

        await SettingsEditorWiring.WaitForExitAsync(
            () => Task.FromResult<IReadOnlyList<Pane>>(++polls < 3 ? [Listed(Nvim)] : []),
            Nvim,
            () => exits++,
            Tick,
            TimeSpan.FromMinutes(1));

        Assert.Equal(3, polls);
        Assert.Equal(1, exits);
    }

    [Fact]
    public async Task A_multiplexer_that_throws_still_fires_the_exit()
    {
        var exits = 0;

        await SettingsEditorWiring.WaitForExitAsync(
            () => throw new IOException("fleetd went away"),
            Nvim,
            () => exits++,
            Tick,
            TimeSpan.FromMinutes(1));

        Assert.Equal(1, exits);
    }

    [Fact]
    public async Task A_pane_that_never_closes_fires_the_exit_at_the_deadline()
    {
        var exits = 0;

        await SettingsEditorWiring.WaitForExitAsync(
            () => Task.FromResult<IReadOnlyList<Pane>>([Listed(Nvim)]),
            Nvim,
            () => exits++,
            Tick,
            TimeSpan.FromMilliseconds(30));

        Assert.Equal(1, exits);
    }
    private const string File = "/home/me/.fleet/techweb/settings.json";

    private sealed class FakeCode(bool available = true, string? trouble = null) : IFileEditor
    {
        public List<string> Edited { get; } = [];

        public Action? Closed { get; private set; }

        public bool Available => available;

        public string? LaunchDetached(string folder) => null;

        public string? EditAndWait(string file, Action closed)
        {
            Edited.Add(file);
            Closed = closed;
            return trouble;
        }
    }

    [Fact]
    public void With_vscode_the_settings_file_is_edited_in_code_and_reloads_after_it_closes()
    {
        var code = new FakeCode();
        var toasts = new List<string>();
        var exits = 0;

        var trouble = SettingsEditorWiring.OpenInVscode(code, File, () => exits++, toasts.Add, windows: false);

        Assert.Null(trouble);
        Assert.Equal([File], code.Edited);
        Assert.Equal(0, exits);
        code.Closed!();
        Assert.Equal(1, exits);
        Assert.Empty(toasts);
    }

    [Fact]
    public void Without_code_on_path_an_install_hint_is_toasted_and_nothing_starts()
    {
        var code = new FakeCode(available: false);
        var toasts = new List<string>();

        var trouble = SettingsEditorWiring.OpenInVscode(code, File, () => { }, toasts.Add, windows: false);

        Assert.Equal(EditorChoice.InstallHint(windows: false), trouble);
        Assert.Equal([EditorChoice.InstallHint(windows: false)], toasts);
        Assert.Empty(code.Edited);
    }

    [Fact]
    public void A_code_that_fails_to_start_is_toasted()
    {
        var code = new FakeCode(trouble: "could not start code: nope");
        var toasts = new List<string>();

        var trouble = SettingsEditorWiring.OpenInVscode(code, File, () => { }, toasts.Add, windows: false);

        Assert.Equal("could not start code: nope", trouble);
        Assert.Equal(["could not start code: nope"], toasts);
    }

    [Fact]
    public void With_vscode_and_no_code_nvim_is_never_spawned()
    {
        var nvims = 0;
        var toasts = new List<string>();
        var spawn = SettingsEditorWiring.SpawnFor(
            Editor.Vscode,
            _ =>
            {
                nvims++;
                return null;
            },
            exited => SettingsEditorWiring.OpenInVscode(new FakeCode(available: false), File, exited, toasts.Add, windows: false));

        var trouble = spawn(() => { });

        Assert.Equal(0, nvims);
        Assert.Equal(EditorChoice.InstallHint(windows: false), trouble);
        Assert.Single(toasts);
    }

    [Fact]
    public void With_nvim_the_pane_spawner_is_used()
    {
        var nvims = 0;
        var codes = 0;
        var spawn = SettingsEditorWiring.SpawnFor(
            Editor.Nvim,
            _ =>
            {
                nvims++;
                return null;
            },
            _ =>
            {
                codes++;
                return null;
            });

        spawn(() => { });

        Assert.Equal(1, nvims);
        Assert.Equal(0, codes);
    }
    [Fact]
    public void The_shared_launcher_can_wait_on_a_file()
    {
        IFileEditor code = Adapters.EditorLauncher();

        Assert.NotNull(code);
    }

    [Fact]
    public void The_shared_settings_store_can_reload_and_check_the_file()
    {
        var store = Adapters.Settings();

        Assert.False(store.Exists("no-such-project-" + Guid.NewGuid().ToString("N")));
    }
}
