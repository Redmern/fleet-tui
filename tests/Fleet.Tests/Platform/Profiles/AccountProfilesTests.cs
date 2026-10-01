using Fleet.Platform.Profiles;
using Fleet.Ports.Projects.Models;

namespace Fleet.Tests.Platform.Profiles;

public sealed class AccountProfilesTests
{
    private static readonly string Base = Path.Combine(Path.GetTempPath(), "fleet-profiles");
    private static readonly string Personal = Path.Combine(Base, "Personal");
    private static readonly string Rib = Path.Combine(Base, "repos", "rib");
    private static readonly string RibProjects = Path.Combine(Base, "Projects", "RIB");
    private static readonly string Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    // The shape of a real ~/.profiles.psd1: comments, nested tables, lists, '' escapes.
    private static readonly string Psd1 = $$"""
        <# header
           block comment #>
        @{
            # Claude profile used where no Roots match.
            Default     = 'work'
            GitFallback = @{ Name = 'Some One'; Email = 'some@example.com' }
            Profiles    = @(
                @{
                    Name     = 'personal'
                    Aliases  = @('pers')
                    Roots    = @('{{Personal}}\')
                    Claude   = '~\.claude-personal'
                    Enabled  = $true
                }
                @{
                    Name     = 'rib'
                    Roots    = @('{{Rib}}', '{{RibProjects}}')
                    Remotes  = @(
                        'https://dev.azure.com/rib-products/**'
                    )
                    Claude   = '~\.claude-rib'
                    Note     = 'it''s quoted'
                }
                @{
                    Name     = 'work'
                    Aliases  = @('triv', 'trivium')
                    Roots    = @()
                    Claude   = '~\.claude'
                    Extra    = $null
                }
            )
        }
        """;

    private static AccountProfiles Profiles() => AccountProfiles.Parse(Psd1);

    [Fact]
    public void The_data_file_parser_reads_tables_lists_strings_and_skips_comments()
    {
        var data = (Dictionary<string, object?>)PowerShellData.Parse(Psd1)!;
        var profiles = (List<object?>)data["Profiles"]!;
        var rib = (Dictionary<string, object?>)profiles[1]!;

        Assert.Equal("work", data["default"]);
        Assert.Equal("some@example.com", ((Dictionary<string, object?>)data["GitFallback"]!)["Email"]);
        Assert.Equal(3, profiles.Count);
        Assert.Equal("it's quoted", rib["Note"]);
        Assert.Equal(true, ((Dictionary<string, object?>)profiles[0]!)["Enabled"]);
        Assert.Null(((Dictionary<string, object?>)profiles[2]!)["Extra"]);
    }

    [Fact]
    public void A_broken_data_file_says_on_which_line()
    {
        var error = Assert.Throws<FormatException>(() => PowerShellData.Parse("@{\n  Default = 'work'\n  Broken 'x'\n}"));
        Assert.Contains("line 3", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_longest_root_containing_the_folder_wins_and_otherwise_the_default()
    {
        var profiles = Profiles();

        Assert.Equal("personal", profiles.ForFolder(Path.Combine(Personal, "repos", "fleet"))!.Name);
        Assert.Equal("rib", profiles.ForFolder(Path.Combine(Rib, "Upskilling"))!.Name);
        Assert.Equal("rib", profiles.ForFolder(RibProjects)!.Name);
        Assert.Equal("work", profiles.ForFolder(RibProjects + "X")!.Name);
        Assert.Equal("work", profiles.ForFolder(Path.Combine(Base, "elsewhere"))!.Name);
    }

    [Fact]
    public void Aliases_find_a_profile_and_the_default_claude_folder_means_leave_it_unset()
    {
        var profiles = Profiles();

        Assert.Equal("personal", profiles.Named("PERS")!.Name);
        Assert.Equal("work", profiles.Named("triv")!.Name);
        Assert.Equal(Path.Combine(Home, ".claude-rib"), AccountProfiles.ConfigDir(profiles.Named("rib")));
        Assert.Null(AccountProfiles.ConfigDir(profiles.Named("work")));
    }

    [Fact]
    public void A_folder_profile_is_marked_automatic_and_clears_a_pin_from_the_shell()
    {
        var env = PaneProfile.Env(Path.Combine(Rib, "x"), [], Profiles())!;

        Assert.Equal(Path.Combine(Home, ".claude-rib"), env[AccountProfiles.ConfigDirVariable]);
        Assert.Equal("1", env[AccountProfiles.AutoVariable]);
        Assert.Equal(string.Empty, env[AccountProfiles.PinnedVariable]);
    }

    [Fact]
    public void The_default_profile_removes_claude_config_dir_so_claude_uses_its_own_default()
    {
        var env = PaneProfile.Env(Path.Combine(Base, "elsewhere"), [], Profiles())!;

        Assert.Equal(string.Empty, env[AccountProfiles.ConfigDirVariable]);
        Assert.Equal(string.Empty, env[AccountProfiles.AutoVariable]);
    }

    [Fact]
    public void A_project_pinned_to_a_profile_wins_over_its_folder()
    {
        var root = Path.Combine(Personal, "client-work");
        Project[] projects = [new("client", root, "rib"), new("personal-side", Path.Combine(Personal, "side"))];

        var pinned = PaneProfile.Env(Path.Combine(root, "backend", "feature"), projects, Profiles())!;
        var unpinned = PaneProfile.Env(Path.Combine(Personal, "side"), projects, Profiles())!;

        Assert.Equal("rib", pinned[AccountProfiles.PinnedVariable]);
        Assert.Equal(Path.Combine(Home, ".claude-rib"), pinned[AccountProfiles.ConfigDirVariable]);
        Assert.Equal(Path.Combine(Home, ".claude-personal"), unpinned[AccountProfiles.ConfigDirVariable]);
    }

    [Fact]
    public void Without_a_profiles_file_panes_keep_the_environment_they_get()
    {
        Assert.Null(PaneProfile.Env(Personal, [], null));
    }
}
