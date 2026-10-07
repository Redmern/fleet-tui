using Fleet.Platform.Themes;
using Fleet.Ports.Themes.Enums;

namespace Fleet.Tests.Platform.Themes;

public sealed class OmarchyThemeSourceTests : IDisposable
{
    private const string Line = "'/usr/bin/fleet' theme sync >/dev/null 2>&1 || true";

    private readonly string _home = Path.Combine(Path.GetTempPath(), "fleet-tests", Path.GetRandomFileName());

    public void Dispose()
    {
        if (Directory.Exists(_home))
        {
            Directory.Delete(_home, recursive: true);
        }
    }

    private string ThemeDir => Path.Combine(_home, ".config", "omarchy", "current", "theme");

    [Fact]
    public void Without_omarchy_there_is_no_current_theme() =>
        Assert.Null(new OmarchyThemeSource(_home).Current());

    [Fact]
    public void The_current_theme_carries_its_name_colors_and_light_marker()
    {
        Directory.CreateDirectory(ThemeDir);
        File.WriteAllText(Path.Combine(_home, ".config", "omarchy", "current", "theme.name"), "rose-pine\n");
        File.WriteAllText(Path.Combine(ThemeDir, "colors.toml"), "background = \"#faf4ed\"");
        File.WriteAllText(Path.Combine(ThemeDir, "light.mode"), string.Empty);

        var snapshot = new OmarchyThemeSource(_home).Current()!;

        Assert.Equal("rose-pine", snapshot.Name);
        Assert.Contains("#faf4ed", snapshot.ColorsToml, StringComparison.Ordinal);
        Assert.Null(snapshot.AlacrittyToml);
        Assert.True(snapshot.LightMarker);
    }

    [Fact]
    public void A_missing_hook_is_created_as_a_script()
    {
        var source = new OmarchyThemeSource(_home);

        Assert.Equal(HookInstall.Created, source.InstallHook(Line, "theme sync"));
        Assert.Equal($"#!/bin/bash\n{Line}\n", File.ReadAllText(source.HookFile));
    }

    [Fact]
    public void An_existing_hook_keeps_its_lines_and_gains_fleet_once()
    {
        var source = new OmarchyThemeSource(_home);

        Directory.CreateDirectory(Path.GetDirectoryName(source.HookFile)!);
        File.WriteAllText(source.HookFile, "#!/bin/bash\nnotify-send \"theme $1\"\n");

        Assert.Equal(HookInstall.Appended, source.InstallHook(Line, "theme sync"));
        Assert.Equal(HookInstall.Already, source.InstallHook(Line, "theme sync"));
        Assert.Equal($"#!/bin/bash\nnotify-send \"theme $1\"\n{Line}\n", File.ReadAllText(source.HookFile));
    }

    [Fact]
    public void A_hook_pointing_at_an_old_fleet_is_repointed_in_place()
    {
        var source = new OmarchyThemeSource(_home);
        const string tail = " theme sync >/dev/null 2>&1 || true";

        Directory.CreateDirectory(Path.GetDirectoryName(source.HookFile)!);
        File.WriteAllText(source.HookFile, $"#!/bin/bash\n'/old/fleet'{tail}\necho done\n");

        Assert.Equal(HookInstall.Updated, source.InstallHook($"'/new/fleet'{tail}", tail));
        Assert.Equal($"#!/bin/bash\n'/new/fleet'{tail}\necho done\n", File.ReadAllText(source.HookFile));
    }

    [Fact]
    public void A_hook_that_only_mentions_theme_sync_still_gains_fleet()
    {
        var source = new OmarchyThemeSource(_home);
        const string tail = " theme sync >/dev/null 2>&1 || true";

        Directory.CreateDirectory(Path.GetDirectoryName(source.HookFile)!);
        File.WriteAllText(source.HookFile, "#!/bin/bash\n# todo: fleet theme sync\n");

        Assert.Equal(HookInstall.Appended, source.InstallHook($"'/usr/bin/fleet'{tail}", tail));
    }

    [Fact]
    public void Chaining_goes_before_a_trailing_exit()
    {
        var chained = OmarchyThemeSource.Chain("#!/bin/bash\nfoo\nexit 0\n\n", "fleet theme sync");

        Assert.Equal("#!/bin/bash\nfoo\nfleet theme sync\nexit 0\n", chained);
    }
}
