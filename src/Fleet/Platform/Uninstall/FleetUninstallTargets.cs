using System.Text;
using Fleet.Platform.Uninstall.Models;
using Fleet.Ports.Uninstall;
using Fleet.Ports.Uninstall.Enums;
using Fleet.Ports.Uninstall.Models;
using Fleet.Shared.Constants;

namespace Fleet.Platform.Uninstall;

public sealed class FleetUninstallTargets(
    UninstallEnvironment environment,
    IDaemonStopper daemon,
    IUserPathVariable userPath) : IUninstallTargets
{
    private const string DaemonName = "fleetd";

    private const string ShortcutName = "Fleet.lnk";

    private const string Launcher = "Start-Fleet.ps1";

    private static readonly string NvimApp = AgentHarness.FleetNvimAppName;

    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private string LocalAppData => environment.Directory("LOCALAPPDATA", "AppData", "Local");

    private string AppData => environment.Directory("APPDATA", "AppData", "Roaming");

    private string InstallDir => Path.Combine(LocalAppData, "Programs", "fleet");

    public IReadOnlyList<UninstallItem> Discover()
    {
        List<UninstallItem> items = [new(UninstallKind.Daemon, DaemonName, "the fleet daemon, stopped if it runs")];

        items.AddRange(NvimDirectories()
            .Where(d => System.IO.Directory.Exists(d.Path))
            .Select(d => new UninstallItem(UninstallKind.Directory, d.Path, d.Label)));

        if (ConfigHomeRefusal() is null && System.IO.Directory.Exists(environment.ConfigHome))
        {
            items.Add(new(UninstallKind.Directory, environment.ConfigHome, "the fleet config home"));
        }

        items.AddRange(environment.Windows ? WindowsItems() : ShellRcItems());

        if (IsFleet(environment.Executable) && File.Exists(environment.Executable))
        {
            items.Add(new(UninstallKind.Binary, environment.Executable, "the fleet binary"));
        }

        return items;
    }

    public IReadOnlyList<string> Refusals() =>
        ConfigHomeRefusal() is { } reason ? [$"left out the fleet config home {environment.ConfigHome}: {reason}"] : [];

    public void Remove(UninstallItem item)
    {
        switch (item.Kind)
        {
            case UninstallKind.Daemon:
                StopDaemon();
                break;
            case UninstallKind.Directory:
                RemoveDirectory(item.Path);
                break;
            case UninstallKind.ShellRc:
                ShellRcLines.Strip(item.Path, deleteWhenEmpty: Path.GetFileName(item.Path) == "fleet.fish");
                break;
            case UninstallKind.UserPath:
                RemoveFromUserPath(item.Path);
                break;
            case UninstallKind.Shortcut:
                RemoveShortcut(item.Path);
                break;
            case UninstallKind.Binary:
                RemoveBinary(item.Path);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(item), item.Kind, "unknown uninstall item");
        }
    }

    public static void SetAside(string path, Action<string> delete)
    {
        try
        {
            delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            File.Move(path, $"{path}.old-{DateTime.UtcNow:yyyyMMddHHmmss}", overwrite: true);
        }
    }

    private string? ConfigHomeRefusal()
    {
        var configHome = environment.ConfigHome;

        if (string.IsNullOrWhiteSpace(configHome) || !Path.IsPathFullyQualified(configHome))
        {
            return "it is not an absolute path";
        }

        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(configHome));
        var home = Path.TrimEndingDirectorySeparator(Path.GetFullPath(environment.Home));

        if (string.Equals(full, Path.TrimEndingDirectorySeparator(Path.GetPathRoot(full) ?? ""), PathComparison))
        {
            return "it is a filesystem root";
        }

        if (string.Equals(full, home, PathComparison) || Contains(full, home))
        {
            return "it is the home directory or one of its parents";
        }

        return null;
    }

    private IEnumerable<(string Path, string Label)> NvimDirectories()
    {
        if (environment.Windows)
        {
            var local = LocalAppData;

            return
            [
                (Path.Combine(environment.Or("XDG_CONFIG_HOME", local), NvimApp), "the fleet-nvim config"),
                (Path.Combine(environment.Or("XDG_DATA_HOME", local), $"{NvimApp}-data"), "the fleet-nvim data and state"),
                (Path.Combine(environment.Or("XDG_CACHE_HOME", environment.Or("TEMP", Path.Combine(local, "Temp"))), NvimApp), "the fleet-nvim cache"),
            ];
        }

        return
        [
            (Path.Combine(environment.Directory("XDG_CONFIG_HOME", ".config"), NvimApp), "the fleet-nvim config"),
            (Path.Combine(environment.Directory("XDG_DATA_HOME", ".local", "share"), NvimApp), "the fleet-nvim data"),
            (Path.Combine(environment.Directory("XDG_STATE_HOME", ".local", "state"), NvimApp), "the fleet-nvim state"),
            (Path.Combine(environment.Directory("XDG_CACHE_HOME", ".cache"), NvimApp), "the fleet-nvim cache"),
        ];
    }

    private IEnumerable<UninstallItem> ShellRcItems()
    {
        string[] rcs =
        [
            Path.Combine(environment.Home, ".bashrc"),
            Path.Combine(environment.Directory("ZDOTDIR"), ".zshenv"),
            Path.Combine(environment.Home, ".profile"),
            Path.Combine(environment.Directory("XDG_CONFIG_HOME", ".config"), "fish", "conf.d", "fleet.fish"),
        ];

        return rcs
            .Where(ShellRcLines.IsMarked)
            .Select(rc => new UninstallItem(UninstallKind.ShellRc, rc, "the fleet PATH line"));
    }

    private IEnumerable<UninstallItem> WindowsItems()
    {
        if (UserPathEntries().Any(IsInstallDir))
        {
            yield return new(UninstallKind.UserPath, InstallDir, "the user PATH entry");
        }

        string[] shortcuts =
        [
            Path.Combine(AppData, "Microsoft", "Windows", "Start Menu", "Programs", ShortcutName),
            Path.Combine(AppData, "Microsoft", "Internet Explorer", "Quick Launch", "User Pinned", "TaskBar", ShortcutName),
        ];

        foreach (var shortcut in shortcuts.Where(IsOurShortcut))
        {
            yield return new(UninstallKind.Shortcut, shortcut, "the fleet shortcut");
        }

        var fragment = Path.Combine(LocalAppData, "Microsoft", "Windows Terminal", "Fragments", "Fleet");

        if (System.IO.Directory.Exists(fragment))
        {
            yield return new(UninstallKind.Shortcut, fragment, "the Windows Terminal profile");
        }

        if (System.IO.Directory.Exists(InstallDir))
        {
            yield return new(UninstallKind.Directory, InstallDir, "the install directory");
        }
    }

    private void StopDaemon()
    {
        if (!daemon.Stop())
        {
            throw new IOException($"{DaemonName} is still running");
        }
    }

    private string[] UserPathEntries() =>
        (userPath.Get() ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries);

    private bool IsInstallDir(string entry) =>
        string.Equals(entry.TrimEnd('\\', '/'), InstallDir.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

    private void RemoveFromUserPath(string directory)
    {
        var entries = UserPathEntries();
        var kept = entries.Where(e => !IsInstallDir(e)).ToArray();

        if (kept.Length != entries.Length)
        {
            userPath.Set(string.Join(';', kept));
        }
    }

    private bool IsOurShortcut(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        var bytes = File.ReadAllBytes(path);
        var launcher = Path.Combine(InstallDir, Launcher);

        return Encoding.Unicode.GetString(bytes).Contains(launcher, StringComparison.OrdinalIgnoreCase)
            || Encoding.Latin1.GetString(bytes).Contains(launcher, StringComparison.OrdinalIgnoreCase);
    }

    private static void RemoveShortcut(string path)
    {
        if (System.IO.Directory.Exists(path))
        {
            System.IO.Directory.Delete(path, recursive: true);
            return;
        }

        File.Delete(path);
    }

    private void RemoveDirectory(string path)
    {
        if (!System.IO.Directory.Exists(path))
        {
            return;
        }

        foreach (var file in System.IO.Directory.EnumerateFiles(path, "*", Everything()))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        if (Contains(path, environment.Executable))
        {
            RemoveAllBut(path, environment.Executable);
            return;
        }

        System.IO.Directory.Delete(path, recursive: true);
    }

    private static void RemoveAllBut(string directory, string keep)
    {
        foreach (var file in System.IO.Directory.EnumerateFiles(directory))
        {
            if (!string.Equals(file, keep, PathComparison))
            {
                File.Delete(file);
            }
        }

        foreach (var child in System.IO.Directory.EnumerateDirectories(directory))
        {
            if (Contains(child, keep))
            {
                RemoveAllBut(child, keep);
                continue;
            }

            System.IO.Directory.Delete(child, recursive: true);
        }
    }

    private void RemoveBinary(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        SetAside(path, File.Delete);

        var parent = Path.GetDirectoryName(path);

        if (environment.Windows
            && parent is not null
            && string.Equals(parent, InstallDir, StringComparison.OrdinalIgnoreCase)
            && !System.IO.Directory.EnumerateFileSystemEntries(parent).Any())
        {
            System.IO.Directory.Delete(parent);
        }
    }

    private static bool Contains(string directory, string path) =>
        path.StartsWith(Path.TrimEndingDirectorySeparator(directory) + Path.DirectorySeparatorChar, PathComparison);

    private static EnumerationOptions Everything() =>
        new() { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = true };

    private static bool IsFleet(string path) =>
        string.Equals(Path.GetFileNameWithoutExtension(path), "fleet", StringComparison.OrdinalIgnoreCase);
}
