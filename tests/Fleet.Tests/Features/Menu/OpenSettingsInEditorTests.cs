using Fleet.Features.Menu.EditSettings;
using Fleet.Shared.Settings.Enums;
using Fleet.Shared.Settings.Models;

namespace Fleet.Tests.Features.Menu;

public class OpenSettingsInEditorTests
{
    private sealed class FakeDisk
    {
        public bool Exists { get; set; }

        public List<SettingsConfig> Saved { get; } = [];

        public SettingsConfig? OnDisk { get; set; }

        public List<SettingsConfig> Resynced { get; } = [];

        public string? ResyncTrouble { get; set; }
    }

    private sealed class FakeSpawner
    {
        public string? Trouble { get; set; }

        public int Spawns { get; private set; }

        public Action? Exited { get; private set; }

        public string? Spawn(Action exited)
        {
            Spawns++;
            Exited = exited;
            return Trouble;
        }
    }

    private static OpenSettingsInEditor Editor(FakeDisk disk, FakeSpawner spawner) =>
        new(
            () => disk.Exists,
            disk.Saved.Add,
            spawner.Spawn,
            () => disk.OnDisk,
            next =>
            {
                disk.Resynced.Add(next);
                return disk.ResyncTrouble;
            });

    private static readonly SettingsConfig Edited =
        SettingsConfig.Default.With(HarnessTool.NewAgent, ActionPolicy.Forbid);

    [Fact]
    public void Opening_saves_the_current_settings_first_when_the_file_is_missing()
    {
        var disk = new FakeDisk { Exists = false };
        var spawner = new FakeSpawner();
        var current = Edited;

        var trouble = Editor(disk, spawner).Open(current, () => { });

        Assert.Null(trouble);
        Assert.Equal([current], disk.Saved);
        Assert.Equal(1, spawner.Spawns);
    }

    [Fact]
    public void Opening_leaves_an_existing_file_alone()
    {
        var disk = new FakeDisk { Exists = true };
        var spawner = new FakeSpawner();

        Editor(disk, spawner).Open(SettingsConfig.Default, () => { });

        Assert.Empty(disk.Saved);
        Assert.Equal(1, spawner.Spawns);
    }

    [Fact]
    public void Opening_reports_why_the_multiplexer_could_not_spawn_nvim()
    {
        var disk = new FakeDisk { Exists = true };
        var spawner = new FakeSpawner { Trouble = "fleet needs a multiplexer." };

        var trouble = Editor(disk, spawner).Open(SettingsConfig.Default, () => { });

        Assert.Equal("fleet needs a multiplexer.", trouble);
    }

    [Fact]
    public void The_exit_callback_reaches_the_spawner()
    {
        var disk = new FakeDisk { Exists = true };
        var spawner = new FakeSpawner();
        var exits = 0;

        Editor(disk, spawner).Open(SettingsConfig.Default, () => exits++);
        spawner.Exited!();

        Assert.Equal(1, exits);
    }

    [Fact]
    public void Reloading_takes_the_settings_on_disk_and_resyncs_them()
    {
        var disk = new FakeDisk { Exists = true, OnDisk = Edited };
        var editor = Editor(disk, new FakeSpawner());

        var reloaded = editor.Reload(SettingsConfig.Default);

        Assert.Same(Edited, reloaded.Config);
        Assert.Equal([Edited], disk.Resynced);
        Assert.Equal(OpenSettingsInEditor.Reloaded, reloaded.Message);
    }

    [Fact]
    public void An_unparsable_file_keeps_the_previous_settings_and_skips_the_resync()
    {
        var disk = new FakeDisk { Exists = true, OnDisk = null };
        var previous = Edited;

        var reloaded = Editor(disk, new FakeSpawner()).Reload(previous);

        Assert.Same(previous, reloaded.Config);
        Assert.Empty(disk.Resynced);
        Assert.Equal(OpenSettingsInEditor.Unparsable, reloaded.Message);
    }

    [Fact]
    public void A_failed_resync_is_reported_after_reloading()
    {
        var disk = new FakeDisk { Exists = true, OnDisk = Edited, ResyncTrouble = "claude settings are locked" };

        var reloaded = Editor(disk, new FakeSpawner()).Reload(SettingsConfig.Default);

        Assert.Same(Edited, reloaded.Config);
        Assert.Equal("claude settings are locked", reloaded.Message);
    }

    [Fact]
    public void Without_a_resync_reloading_only_reads_the_file()
    {
        var disk = new FakeDisk { Exists = true, OnDisk = Edited };
        var editor = new OpenSettingsInEditor(
            () => disk.Exists, disk.Saved.Add, new FakeSpawner().Spawn, () => disk.OnDisk, OpenSettingsInEditor.NoResync);

        var reloaded = editor.Reload(SettingsConfig.Default);

        Assert.Same(Edited, reloaded.Config);
        Assert.Equal(OpenSettingsInEditor.Reloaded, reloaded.Message);
    }
    private static OpenSettingsInEditor For(Fleet.Shared.Settings.Enums.Editor editor) =>
        new(() => true, _ => { }, _ => null, () => null, OpenSettingsInEditor.NoResync, editor);

    [Fact]
    public void With_nvim_the_status_and_label_name_nvim()
    {
        var editor = For(Fleet.Shared.Settings.Enums.Editor.Nvim);

        Assert.Equal("Opened in nvim; the list reloads when you quit it.", editor.Opened);
        Assert.Equal("open in nvim", editor.OpenLabel);
    }

    [Fact]
    public void With_vscode_the_status_and_label_name_vs_code()
    {
        var editor = For(Fleet.Shared.Settings.Enums.Editor.Vscode);

        Assert.Equal("Opened in VS Code; the list reloads when you close its tab.", editor.Opened);
        Assert.Equal("open in vs code", editor.OpenLabel);
        Assert.DoesNotContain("nvim", editor.Opened, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Without_an_editor_it_stays_nvim()
    {
        var editor = new OpenSettingsInEditor(() => true, _ => { }, _ => null, () => null, OpenSettingsInEditor.NoResync);

        Assert.Equal("open in nvim", editor.OpenLabel);
    }
}
