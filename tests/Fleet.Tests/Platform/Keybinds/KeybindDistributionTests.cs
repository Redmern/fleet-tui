using Fleet.Platform.Claude;
using Fleet.Platform.Keybinds;
using Fleet.Ports.Keybinds.Enums;
using Fleet.Platform.Keybinds.Models;
using Fleet.Ports.Keybinds.Models;
using Fleet.Platform.Nvim;
using Fleet.Shared.Keybinds;
using Fleet.Shared.Keybinds.Enums;

namespace Fleet.Tests.Platform.Keybinds;

public sealed class KeybindDistributionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fleet-tests", Path.GetRandomFileName());

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string NvimDirectory => Path.Combine(_root, "fleet-nvim");

    private string UserModule => Path.Combine(_root, "fleet", NvimKeybinds.UserModuleFile);

    private string ClaudeHome => Path.Combine(_root, ".claude");

    private string ClaudeFile => ClaudeKeybindings.PathIn(ClaudeHome);

    private string Generated => Path.Combine(NvimDirectory, "lua", "fleet", NvimKeybinds.GeneratedFile);

    private KeybindDistribution Distribution(KeybindSet? set = null) =>
        new(set ?? KeybindDefaults.Set, new KeybindTargets(NvimDirectory, UserModule, [ClaudeHome]), KeybindOs.Windows);

    private void InstallNvim()
    {
        Directory.CreateDirectory(NvimDirectory);
        File.WriteAllText(Path.Combine(NvimDirectory, "init.lua"), "-- fleet-nvim");
    }

    [Fact]
    public void Applying_writes_the_generated_file_the_user_module_and_claude()
    {
        InstallNvim();
        Directory.CreateDirectory(ClaudeHome);

        var applied = Distribution().Apply(KeybindDistribution.Rendered, dryRun: false);

        Assert.All(applied, a => Assert.Equal(KeybindOutcome.Written, a.Outcome));
        Assert.Equal([Generated, UserModule, ClaudeFile], applied.Select(a => a.Path));
        Assert.StartsWith(NvimKeybinds.GeneratedHeader, File.ReadAllText(Generated), StringComparison.Ordinal);
        Assert.StartsWith(NvimKeybinds.UserModuleHeader, File.ReadAllText(UserModule), StringComparison.Ordinal);
        Assert.Contains("chat:newline", File.ReadAllText(ClaudeFile), StringComparison.Ordinal);
        Assert.Empty(Distribution().Drift());
    }

    [Fact]
    public void Applying_twice_reports_everything_up_to_date()
    {
        InstallNvim();
        Directory.CreateDirectory(ClaudeHome);
        Distribution().Apply(KeybindDistribution.Rendered, dryRun: false);

        var again = Distribution().Apply(KeybindDistribution.Rendered, dryRun: false);

        Assert.All(again, a => Assert.Equal(KeybindOutcome.Current, a.Outcome));
    }

    [Fact]
    public void A_dry_run_writes_nothing_and_says_what_would_change()
    {
        InstallNvim();
        Directory.CreateDirectory(ClaudeHome);

        var applied = Distribution().Apply(KeybindDistribution.Rendered, dryRun: true);

        Assert.All(applied, a => Assert.Equal(KeybindOutcome.Stale, a.Outcome));
        Assert.False(File.Exists(Generated));
        Assert.False(File.Exists(UserModule));
        Assert.False(File.Exists(ClaudeFile));
        Assert.Contains("would write", applied[0].Line, StringComparison.Ordinal);
    }

    [Fact]
    public void A_target_limits_what_is_written()
    {
        InstallNvim();
        Directory.CreateDirectory(ClaudeHome);

        var applied = Distribution().Apply([KeybindTarget.Claude], dryRun: false);

        Assert.Equal(KeybindTarget.Claude, Assert.Single(applied).Target);
        Assert.False(File.Exists(Generated));
        Assert.False(File.Exists(UserModule));
    }

    [Fact]
    public void Fleet_nvim_and_claude_homes_that_are_not_there_are_skipped_not_created()
    {
        var applied = Distribution().Apply(KeybindDistribution.Rendered, dryRun: false);

        Assert.Equal(KeybindOutcome.Skipped, applied.Single(a => a.Path == Generated).Outcome);
        Assert.Equal(KeybindOutcome.Skipped, applied.Single(a => a.Target == KeybindTarget.Claude).Outcome);
        Assert.False(Directory.Exists(NvimDirectory));
        Assert.False(Directory.Exists(ClaudeHome));
        Assert.True(File.Exists(UserModule));
        Assert.Empty(Distribution().Drift());
    }

    [Fact]
    public void Drift_names_the_files_that_differ_from_the_model()
    {
        InstallNvim();
        Directory.CreateDirectory(ClaudeHome);
        Distribution().Apply(KeybindDistribution.Rendered, dryRun: false);

        var set = KeybindDefaults.Set;
        set = set.With(set.Find("resize-left")! with { Chord = "Alt+y", OsChords = new Dictionary<KeybindOs, string>() });

        var drift = Distribution(set).Drift();

        Assert.Equal([Generated, UserModule], drift.Select(d => d.Path));
        Assert.All(drift, d => Assert.Equal(KeybindOutcome.Stale, d.Outcome));
    }

    [Fact]
    public void A_hand_edited_claude_file_that_lost_fleets_binding_is_drift()
    {
        Directory.CreateDirectory(ClaudeHome);
        Distribution().Apply([KeybindTarget.Claude], dryRun: false);
        File.WriteAllText(ClaudeFile, """{ "bindings": [] }""");

        var drift = Distribution().Drift();

        Assert.Contains(drift, d => d.Path == ClaudeFile && d.Outcome == KeybindOutcome.Stale);
    }

    [Fact]
    public void A_claude_file_that_is_not_json_is_reported_as_failed_drift()
    {
        Directory.CreateDirectory(ClaudeHome);
        File.WriteAllText(ClaudeFile, "{ not json");

        var drift = Distribution().Drift();

        Assert.Contains(drift, d => d.Path == ClaudeFile && d.Outcome == KeybindOutcome.Failed);
        Assert.Equal("{ not json", File.ReadAllText(ClaudeFile));
    }
}
