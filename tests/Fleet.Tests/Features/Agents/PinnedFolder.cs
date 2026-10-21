namespace Fleet.Tests.Features.Agents;

// Keeps a folder from being deleted: on Windows by holding a file inside it open, elsewhere by
// taking write permission off the folder so its entries cannot be unlinked.
public sealed class PinnedFolder : IDisposable
{
    private readonly string _folder;

    private readonly FileStream? _held;

    private readonly UnixFileMode _mode;

    private PinnedFolder(string folder)
    {
        _folder = folder;
        var file = Path.Combine(folder, "pinned.txt");
        File.WriteAllText(file, "in use");

        if (OperatingSystem.IsWindows())
        {
            _held = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None);
            return;
        }

        _mode = File.GetUnixFileMode(folder);
        File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserExecute);
    }

    public static PinnedFolder For(string folder) => new(folder);

    public void Dispose()
    {
        if (_held is not null)
        {
            _held.Dispose();
            return;
        }

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(_folder, _mode);
        }
    }
}
