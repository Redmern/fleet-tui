using Fleet.Platform.Themes;

namespace Fleet.Tests.Platform.Themes;

public sealed class ThemeWatcherTests : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly string _config = Path.Combine(Path.GetTempPath(), "fleet-tests", Path.GetRandomFileName());

    public ThemeWatcherTests() => Directory.CreateDirectory(_config);

    public void Dispose()
    {
        if (Directory.Exists(_config))
        {
            Directory.Delete(_config, recursive: true);
        }
    }

    [Fact]
    public async Task Saving_the_current_theme_fires_once_the_writes_settle()
    {
        var fired = new SemaphoreSlim(0);
        using var watcher = new ThemeWatcher(_config, () => fired.Release());

        new FileThemeStore(_config).SaveCurrent("nord");
        File.WriteAllText(Path.Combine(_config, "current-theme"), "dracula\n");

        Assert.True(await fired.WaitAsync(Patience));
    }

    [Fact]
    public async Task Editing_a_custom_theme_fires()
    {
        var fired = new SemaphoreSlim(0);
        using var watcher = new ThemeWatcher(_config, () => fired.Release());

        File.WriteAllText(Path.Combine(_config, "themes", "mine.toml"), "base = \"#000000\"\n");

        Assert.True(await fired.WaitAsync(Patience));
    }

    [Fact]
    public async Task Other_files_in_the_config_dir_do_not_fire()
    {
        var fired = new SemaphoreSlim(0);
        using var watcher = new ThemeWatcher(_config, () => fired.Release());

        File.WriteAllText(Path.Combine(_config, "fleet.log"), "noise\n");

        Assert.False(await fired.WaitAsync(ThemeWatcher.Settle * 4));
    }
}
