using System.Text;
using Fleet.Platform.Uninstall;
using Fleet.Platform.Uninstall.Models;
using Fleet.Ports.Uninstall;
using Fleet.Ports.Uninstall.Enums;
using Fleet.Ports.Uninstall.Models;

namespace Fleet.Tests.Platform.Uninstall;

public sealed class FleetUninstallTargetsTests : IDisposable
{
    private const string Mark = "# added by fleet installer";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"fleet-uninstall-{Guid.NewGuid():N}");

    private readonly Dictionary<string, string> _variables = [];

    private readonly FakeDaemon _daemon = new();

    private readonly FakeUserPath _userPath = new();

    public FleetUninstallTargetsTests() => Directory.CreateDirectory(Home);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Home => Path.Combine(_dir, "home");

    private string ConfigHome => Path.Combine(Home, ".config", "fleet");

    private sealed class FakeDaemon : IDaemonStopper
    {
        public int Stops { get; private set; }

        public bool StillRunning { get; set; }

        public bool Stop()
        {
            Stops++;
            return !StillRunning;
        }
    }

    private sealed class FakeUserPath : IUserPathVariable
    {
        public string? Value { get; set; }

        public string? Get() => Value;

        public void Set(string value) => Value = value;
    }

    private FleetUninstallTargets Targets(string? executable = null, bool windows = false) =>
        new(
            new UninstallEnvironment(
                executable ?? Path.Combine(_dir, "gone"),
                Home,
                ConfigHome,
                windows,
                name => _variables.GetValueOrDefault(name)),
            _daemon,
            _userPath);

    private string MakeDirectory(params string[] parts)
    {
        var path = Path.Combine([Home, .. parts]);
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "file"), "x");
        return path;
    }

    private string MakeFile(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private static void RemoveAll(FleetUninstallTargets targets)
    {
        foreach (var item in targets.Discover())
        {
            targets.Remove(item);
        }
    }

    [Fact]
    public void Discovers_the_running_binary()
    {
        var binary = MakeFile(Path.Combine(_dir, "fleet"), "exe");

        var item = Assert.Single(Targets(binary).Discover(), i => i.Kind == UninstallKind.Binary);

        Assert.Equal(binary, item.Path);
    }

    [Fact]
    public void A_missing_binary_is_not_in_the_plan() =>
        Assert.DoesNotContain(Targets().Discover(), i => i.Kind == UninstallKind.Binary);

    [Theory]
    [InlineData("dotnet")]
    [InlineData("dotnet.exe")]
    public void A_host_that_is_not_fleet_is_never_in_the_plan(string name)
    {
        // Run as `dotnet Fleet.dll`, the process path is the dotnet host, which uninstall must leave alone.
        var host = MakeFile(Path.Combine(_dir, name), "exe");

        Assert.DoesNotContain(Targets(host).Discover(), i => i.Path == host);
    }

    [Theory]
    [InlineData("fleet")]
    [InlineData("fleet.exe")]
    [InlineData("Fleet.exe")]
    public void The_fleet_binary_is_found_by_its_name(string name)
    {
        var binary = MakeFile(Path.Combine(_dir, name), "exe");

        Assert.Single(Targets(binary).Discover(), i => i.Kind == UninstallKind.Binary);
    }

    [Fact]
    public void Removing_the_binary_takes_it_off_its_path()
    {
        var binary = MakeFile(Path.Combine(_dir, "fleet"), "exe");
        var targets = Targets(binary);

        targets.Remove(Assert.Single(targets.Discover(), i => i.Kind == UninstallKind.Binary));

        Assert.False(File.Exists(binary));
        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Fact]
    public void The_daemon_is_stopped_first()
    {
        var items = Targets().Discover();

        Assert.Equal(UninstallKind.Daemon, items[0].Kind);
    }

    [Fact]
    public void Removing_the_daemon_item_stops_it()
    {
        var targets = Targets();

        targets.Remove(targets.Discover()[0]);

        Assert.Equal(1, _daemon.Stops);
    }

    [Fact]
    public void A_daemon_that_will_not_stop_is_a_failure()
    {
        _daemon.StillRunning = true;
        var targets = Targets();

        Assert.ThrowsAny<Exception>(() => targets.Remove(targets.Discover()[0]));
    }

    [Fact]
    public void The_fleet_nvim_directories_in_their_default_places_are_removed_and_the_users_nvim_is_not()
    {
        var config = MakeDirectory(".config", "fleet-nvim");
        var data = MakeDirectory(".local", "share", "fleet-nvim");
        var state = MakeDirectory(".local", "state", "fleet-nvim");
        var cache = MakeDirectory(".cache", "fleet-nvim");
        var users = MakeDirectory(".config", "nvim");
        var usersData = MakeDirectory(".local", "share", "nvim");
        var targets = Targets();

        var paths = targets.Discover().Where(i => i.Kind == UninstallKind.Directory).Select(i => i.Path).ToList();
        RemoveAll(targets);

        Assert.Equal([config, data, state, cache], paths);
        Assert.All([config, data, state, cache], p => Assert.False(Directory.Exists(p)));
        Assert.True(Directory.Exists(users));
        Assert.True(Directory.Exists(usersData));
    }

    [Fact]
    public void The_fleet_nvim_directories_follow_the_xdg_variables()
    {
        _variables["XDG_CONFIG_HOME"] = Path.Combine(Home, "xc");
        _variables["XDG_DATA_HOME"] = Path.Combine(Home, "xd");
        _variables["XDG_STATE_HOME"] = Path.Combine(Home, "xs");
        _variables["XDG_CACHE_HOME"] = Path.Combine(Home, "xk");
        var expected = new[] { MakeDirectory("xc", "fleet-nvim"), MakeDirectory("xd", "fleet-nvim"), MakeDirectory("xs", "fleet-nvim"), MakeDirectory("xk", "fleet-nvim") };
        var ignored = MakeDirectory(".config", "fleet-nvim");

        var paths = Targets().Discover().Where(i => i.Kind == UninstallKind.Directory).Select(i => i.Path);

        Assert.Equal(expected, paths);
        Assert.True(Directory.Exists(ignored));
    }

    [Fact]
    public void Missing_directories_are_not_in_the_plan() =>
        Assert.DoesNotContain(Targets().Discover(), i => i.Kind == UninstallKind.Directory);

    [Fact]
    public void The_config_home_is_removed_after_the_fleet_nvim_directories()
    {
        var nvim = MakeDirectory(".config", "fleet-nvim");
        MakeDirectory(".config", "fleet", "projects");
        var targets = Targets();

        var paths = targets.Discover().Select(i => i.Path).ToList();
        RemoveAll(targets);

        Assert.True(paths.IndexOf(nvim) < paths.IndexOf(ConfigHome));
        Assert.False(Directory.Exists(ConfigHome));
    }

    [Fact]
    public void Only_the_marked_lines_leave_the_shell_rc_files_and_the_rest_stay_byte_for_byte()
    {
        var bashrc = MakeFile(Path.Combine(Home, ".bashrc"), $"export A=1\r\nexport PATH=\"/x:$PATH\" {Mark}\n# mine\n\nlast line no newline");
        var profile = MakeFile(Path.Combine(Home, ".profile"), $"one\nexport PATH=\"/x:$PATH\" {Mark}\n");
        var zshenv = MakeFile(Path.Combine(Home, ".zshenv"), "untouched\n");
        var targets = Targets();

        var rcs = targets.Discover().Where(i => i.Kind == UninstallKind.ShellRc).Select(i => i.Path).ToList();
        RemoveAll(targets);

        Assert.Equal([bashrc, profile], rcs);
        Assert.Equal("export A=1\r\n# mine\n\nlast line no newline", File.ReadAllText(bashrc));
        Assert.Equal("one\n", File.ReadAllText(profile));
        Assert.Equal("untouched\n", File.ReadAllText(zshenv));
    }

    [Fact]
    public void The_zshenv_is_found_under_zdotdir()
    {
        _variables["ZDOTDIR"] = Path.Combine(Home, "zdot");
        var zshenv = MakeFile(Path.Combine(Home, "zdot", ".zshenv"), $"path {Mark}\n");
        MakeFile(Path.Combine(Home, ".zshenv"), $"path {Mark}\n");

        var rc = Assert.Single(Targets().Discover(), i => i.Kind == UninstallKind.ShellRc);

        Assert.Equal(zshenv, rc.Path);
    }

    [Fact]
    public void A_fleet_fish_file_left_empty_is_deleted()
    {
        var fish = MakeFile(Path.Combine(Home, ".config", "fish", "conf.d", "fleet.fish"), $"fish_add_path /x {Mark}\n");

        RemoveAll(Targets());

        Assert.False(File.Exists(fish));
    }

    [Fact]
    public void A_fleet_fish_file_with_other_lines_is_kept()
    {
        var fish = MakeFile(Path.Combine(Home, ".config", "fish", "conf.d", "fleet.fish"), $"set -x A 1\nfish_add_path /x {Mark}\n");

        RemoveAll(Targets());

        Assert.Equal("set -x A 1\n", File.ReadAllText(fish));
    }

    [Fact]
    public void The_binary_is_last_on_linux()
    {
        var binary = MakeFile(Path.Combine(_dir, "fleet"), "exe");
        MakeDirectory(".config", "fleet");
        MakeFile(Path.Combine(Home, ".bashrc"), $"p {Mark}\n");

        var items = Targets(binary).Discover();

        Assert.Equal(UninstallKind.Binary, items[^1].Kind);
        Assert.Equal(
            [UninstallKind.Daemon, UninstallKind.Directory, UninstallKind.ShellRc, UninstallKind.Binary],
            items.Select(i => i.Kind).Distinct());
    }

    [Fact]
    public void Windows_has_no_shell_rc_items()
    {
        MakeFile(Path.Combine(Home, ".bashrc"), $"p {Mark}\n");

        Assert.DoesNotContain(Targets(windows: true).Discover(), i => i.Kind == UninstallKind.ShellRc);
    }

    private string LocalAppData => Path.Combine(Home, "AppData", "Local");

    private string AppData => Path.Combine(Home, "AppData", "Roaming");

    private string InstallDir => Path.Combine(LocalAppData, "Programs", "fleet");

    private void WindowsVariables()
    {
        _variables["LOCALAPPDATA"] = LocalAppData;
        _variables["APPDATA"] = AppData;
        _variables["TEMP"] = Path.Combine(LocalAppData, "Temp");
    }

    [Fact]
    public void Windows_removes_the_fleet_nvim_directories_under_localappdata()
    {
        WindowsVariables();
        var config = MakeDirectory("AppData", "Local", "fleet-nvim");
        var data = MakeDirectory("AppData", "Local", "fleet-nvim-data");
        var cache = MakeDirectory("AppData", "Local", "Temp", "fleet-nvim");
        var users = MakeDirectory("AppData", "Local", "nvim");
        var targets = Targets(windows: true);

        var paths = targets.Discover().Where(i => i.Kind == UninstallKind.Directory).Select(i => i.Path).ToList();
        RemoveAll(targets);

        Assert.Equal([config, data, cache], paths);
        Assert.True(Directory.Exists(users));
    }

    [Fact]
    public void Windows_removes_only_the_install_dir_from_the_user_path()
    {
        WindowsVariables();
        _userPath.Value = $@"C:\tools;{InstallDir};C:\other";
        var targets = Targets(windows: true);

        var item = Assert.Single(targets.Discover(), i => i.Kind == UninstallKind.UserPath);
        targets.Remove(item);

        Assert.Equal(InstallDir, item.Path);
        Assert.Equal(@"C:\tools;C:\other", _userPath.Value);
    }

    [Fact]
    public void Windows_without_the_install_dir_on_path_has_no_user_path_item()
    {
        WindowsVariables();
        _userPath.Value = @"C:\tools";

        Assert.DoesNotContain(Targets(windows: true).Discover(), i => i.Kind == UninstallKind.UserPath);
    }

    [Fact]
    public void Windows_removes_our_start_menu_and_taskbar_shortcuts_and_the_terminal_fragment()
    {
        WindowsVariables();
        var start = Path.Combine(AppData, "Microsoft", "Windows", "Start Menu", "Programs", "Fleet.lnk");
        var pinned = Path.Combine(AppData, "Microsoft", "Internet Explorer", "Quick Launch", "User Pinned", "TaskBar", "Fleet.lnk");
        File.WriteAllBytes(MakeFile(start, ""), Encoding.Unicode.GetBytes($"lnk {Path.Combine(InstallDir, "Start-Fleet.ps1")}"));
        File.WriteAllBytes(MakeFile(pinned, ""), Encoding.Unicode.GetBytes($"lnk {Path.Combine(InstallDir, "Start-Fleet.ps1")}"));
        var fragment = MakeDirectory("AppData", "Local", "Microsoft", "Windows Terminal", "Fragments", "Fleet");
        var targets = Targets(windows: true);

        var shortcuts = targets.Discover().Where(i => i.Kind == UninstallKind.Shortcut).Select(i => i.Path).ToList();
        RemoveAll(targets);

        Assert.Equal([start, pinned, fragment], shortcuts);
        Assert.False(File.Exists(start));
        Assert.False(File.Exists(pinned));
        Assert.False(Directory.Exists(fragment));
    }

    [Fact]
    public void Windows_leaves_a_hand_made_fleet_shortcut_alone()
    {
        WindowsVariables();
        var start = MakeFile(Path.Combine(AppData, "Microsoft", "Windows", "Start Menu", "Programs", "Fleet.lnk"), "points somewhere else");

        RemoveAll(Targets(windows: true));

        Assert.True(File.Exists(start));
    }

    [Fact]
    public void Windows_removes_the_install_dir_and_sets_the_running_binary_aside_last()
    {
        WindowsVariables();
        var binary = MakeFile(Path.Combine(InstallDir, "fleet.exe"), "exe");
        MakeFile(Path.Combine(InstallDir, "Start-Fleet.ps1"), "launcher");
        var targets = Targets(binary, windows: true);

        var items = targets.Discover();
        var install = Assert.Single(items, i => i.Path == InstallDir && i.Kind == UninstallKind.Directory);
        RemoveAll(targets);

        Assert.Equal(UninstallKind.Binary, items[^1].Kind);
        Assert.True(Array.IndexOf([.. items], install) < items.Count - 1);
        Assert.False(File.Exists(Path.Combine(InstallDir, "Start-Fleet.ps1")));
        Assert.False(File.Exists(binary));
        Assert.False(Directory.Exists(InstallDir));
    }

    [Fact]
    public void A_binary_that_cannot_be_deleted_is_renamed_aside_on_windows()
    {
        var binary = MakeFile(Path.Combine(_dir, "fleet.exe"), "exe");

        FleetUninstallTargets.SetAside(binary, _ => throw new UnauthorizedAccessException("running"));

        Assert.False(File.Exists(binary));
        Assert.Single(Directory.GetFiles(_dir, "fleet.exe.old-*"));
    }

    [Fact]
    public void The_config_home_follows_what_it_is_given()
    {
        var config = MakeDirectory("custom-fleet-home");

        var targets = new FleetUninstallTargets(
            new UninstallEnvironment(Path.Combine(_dir, "gone"), Home, config, false, _ => null), _daemon, _userPath);

        Assert.Contains(targets.Discover(), i => i.Path == config && i.Kind == UninstallKind.Directory);
    }

    [Fact]
    public void Every_item_has_a_label()
    {
        WindowsVariables();
        MakeDirectory(".config", "fleet");

        Assert.All(Targets().Discover().Concat(Targets(windows: true).Discover()), i => Assert.False(string.IsNullOrWhiteSpace(i.Label)));
    }

    private static UninstallItem Item(UninstallKind kind, string path) => new(kind, path, "x");

    [Fact]
    public void Removing_something_already_gone_is_not_an_error()
    {
        var targets = Targets();

        targets.Remove(Item(UninstallKind.Directory, Path.Combine(Home, "nope")));
        targets.Remove(Item(UninstallKind.ShellRc, Path.Combine(Home, ".nope")));
        targets.Remove(Item(UninstallKind.Shortcut, Path.Combine(Home, "nope.lnk")));
        targets.Remove(Item(UninstallKind.Binary, Path.Combine(Home, "fleet")));
    }
}
