namespace Fleet.Platform.Themes;

public sealed class ThemeWatcher : IDisposable
{
    public static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(150);

    private readonly FileSystemWatcher _current;
    private readonly FileSystemWatcher _custom;
    private readonly Timer _settle;

    public ThemeWatcher(string config, Action changed)
    {
        var store = new FileThemeStore(config);

        Directory.CreateDirectory(store.ThemesDirectory);

        _settle = new Timer(_ => changed(), null, Timeout.Infinite, Timeout.Infinite);
        _current = Watch(config, FileThemeStore.CurrentFileName);
        _custom = Watch(store.ThemesDirectory, "*.toml");
    }

    public void Dispose()
    {
        _current.Dispose();
        _custom.Dispose();
        _settle.Dispose();
    }

    private FileSystemWatcher Watch(string directory, string filter)
    {
        var watcher = new FileSystemWatcher(directory, filter)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
        };

        watcher.Changed += (_, _) => Poke();
        watcher.Created += (_, _) => Poke();
        watcher.Deleted += (_, _) => Poke();
        watcher.Renamed += (_, _) => Poke();
        watcher.EnableRaisingEvents = true;

        return watcher;
    }

    private void Poke()
    {
        try
        {
            _settle.Change(Settle, Timeout.InfiniteTimeSpan);
        }
        catch (ObjectDisposedException)
        {
        }
    }
}
