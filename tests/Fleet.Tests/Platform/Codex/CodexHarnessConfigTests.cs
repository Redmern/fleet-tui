using Fleet.Platform.Codex;
using Fleet.Ports.Claude.Models;

namespace Fleet.Tests.Platform.Codex;

public sealed class CodexHarnessConfigTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("fleet-codex-").FullName;

    private readonly List<(string Folder, IReadOnlyList<string> Patterns)> _excluded = [];

    public CodexHarnessConfigTests() => Directory.CreateDirectory(Folder);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Folder => Path.Combine(_root, "wt");

    private string ProjectConfig => Path.Combine(Folder, ".codex", "config.toml");

    private string UserConfig => Path.Combine(_root, "home", ".codex", "config.toml");

    private CodexHarnessConfig Config() => new(
        (project, caller) => new McpServerEntry("fleet", "/bin/fleet", ["mcp", "--project", project, "--caller", caller]),
        new Dictionary<string, string> { ["fl-tdd"] = "---\nname: fl-tdd\n---\nbody\r\n" },
        (folder, patterns) => _excluded.Add((folder, patterns)),
        UserConfig);

    private static string Trusted(string path) =>
        $"[projects.\"{path.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"]\ntrust_level = \"trusted\"\n";

    private static string FleetSection(string caller) =>
        "[mcp_servers.fleet]\n"
        + "command = \"/bin/fleet\"\n"
        + $"args = [\"mcp\", \"--project\", \"proj\", \"--caller\", \"{caller}\"]\n"
        + "default_tools_approval_mode = \"approve\"\n";

    private void Seed(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    [Fact]
    public void An_agent_gets_a_project_config_with_the_fleet_server_and_its_caller()
    {
        var written = Config().WriteForAgent(Folder, "proj", "api", "feat/x");

        Assert.True(written.Succeeded, written.Error);
        Assert.Equal(FleetSection("agent:api/feat/x"), File.ReadAllText(ProjectConfig));
    }

    [Fact]
    public void An_orchestration_folder_gets_the_server_with_the_given_caller_and_no_excludes()
    {
        Config().WriteForOrchestration(Folder, "proj", "my-sub");

        Assert.Equal(FleetSection("my-sub"), File.ReadAllText(ProjectConfig));
        Assert.Empty(_excluded);
    }

    [Fact]
    public void An_existing_project_config_keeps_its_other_settings_and_gets_the_new_fleet_command()
    {
        Seed(
            ProjectConfig,
            "model = \"o3\"\n\n[mcp_servers.fleet]\ncommand = \"old\"\n\n[mcp_servers.fleet.env]\nX = \"1\"\n\n"
            + "[mcp_servers.other]\ncommand = \"other\"\n");

        Config().WriteForAgent(Folder, "proj", "api", "main");

        Assert.Equal(
            "model = \"o3\"\n\n" + FleetSection("agent:api/main") + "\n[mcp_servers.fleet.env]\nX = \"1\"\n\n"
            + "[mcp_servers.other]\ncommand = \"other\"\n",
            File.ReadAllText(ProjectConfig));
    }

    [Theory]
    [InlineData("{ not toml")]
    [InlineData("mcp_servers = { fleet = { command = \"x\" } }\n")]
    [InlineData("[mcp_servers]\nfleet = { command = \"x\" }\n")]
    [InlineData("s = \"unterminated\nx = 1\n")]
    public void A_project_config_fleet_cannot_edit_safely_is_left_alone_and_reported(string text)
    {
        Seed(ProjectConfig, text);

        var written = Config().WriteForAgent(Folder, "proj", "api", "main");

        Assert.False(written.Succeeded);
        Assert.Contains(ProjectConfig, written.Error);
        Assert.Equal(text, File.ReadAllText(ProjectConfig));
    }

    [Fact]
    public void An_unreadable_project_config_is_reported()
    {
        Directory.CreateDirectory(ProjectConfig);

        var written = Config().WriteForAgent(Folder, "proj", "api", "main");

        Assert.False(written.Succeeded);
        Assert.True(Directory.Exists(ProjectConfig));
    }

    [Fact]
    public void The_folder_is_trusted_in_the_user_config_without_losing_what_is_there()
    {
        Seed(UserConfig, "[features]\nhooks = true\n");

        Config().WriteForAgent(Folder, "proj", "api", "main");

        Assert.Equal("[features]\nhooks = true\n\n" + Trusted(Folder), File.ReadAllText(UserConfig));
    }

    [Fact]
    public void A_missing_user_config_is_created_with_the_trust_entry()
    {
        Config().WriteForAgent(Folder, "proj", "api", "main");

        Assert.Equal(Trusted(Folder), File.ReadAllText(UserConfig));
    }

    [Fact]
    public void An_already_trusted_folder_leaves_the_user_config_byte_for_byte()
    {
        var text = $"# mine\r\n[projects.'{Folder}']\r\ntrust_level = \"trusted\" # yes\r\n";
        Seed(UserConfig, text);

        Config().WriteForAgent(Folder, "proj", "api", "main");

        Assert.Equal(text, File.ReadAllText(UserConfig));
    }

    [Fact]
    public void A_project_entry_without_a_trust_level_gets_one_under_its_header()
    {
        Seed(UserConfig, $"[projects.\"{Folder}\"]\nnote = 1\n\n[tui]\nx = 1\n");

        Config().WriteForAgent(Folder, "proj", "api", "main");

        Assert.Equal(
            $"[projects.\"{Folder}\"]\ntrust_level = \"trusted\"\nnote = 1\n\n[tui]\nx = 1\n",
            File.ReadAllText(UserConfig));
    }

    [Fact]
    public void A_folder_the_user_marked_untrusted_stays_untrusted_and_is_reported()
    {
        var text = $"[projects.\"{Folder}\"]\ntrust_level = \"untrusted\"\n";
        Seed(UserConfig, text);

        var written = Config().WriteForAgent(Folder, "proj", "api", "main");

        Assert.False(written.Succeeded);
        Assert.Contains("untrusted", written.Error);
        Assert.Equal(text, File.ReadAllText(UserConfig));
    }

    [Theory]
    [InlineData("[broken\n")]
    [InlineData("projects = { }\n")]
    public void A_user_config_fleet_cannot_edit_safely_is_left_alone_and_reported(string text)
    {
        Seed(UserConfig, text);

        var written = Config().WriteForAgent(Folder, "proj", "api", "main");

        Assert.False(written.Succeeded);
        Assert.Contains(UserConfig, written.Error);
        Assert.Equal(text, File.ReadAllText(UserConfig));
        Assert.True(File.Exists(ProjectConfig));
    }

    [Fact]
    public void A_folder_path_with_quotes_and_backslashes_is_escaped()
    {
        var odd = Path.Combine(_root, "we\"ird\\dir");
        Directory.CreateDirectory(odd);

        Config().WriteForAgent(odd, "proj", "api", "main");
        Config().WriteForAgent(odd, "proj", "api", "main");

        Assert.Equal(Trusted(odd), File.ReadAllText(UserConfig));
    }

    [Fact]
    public void A_folder_reached_through_a_symlink_is_trusted_under_its_real_path()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var link = Path.Combine(_root, "link");
        Directory.CreateSymbolicLink(link, _root);

        Config().WriteForAgent(Path.Combine(link, "wt"), "proj", "api", "main");

        Assert.Equal(Trusted(Folder), File.ReadAllText(UserConfig));
    }

    [Fact]
    public void Writing_twice_changes_nothing_the_second_time()
    {
        Seed(UserConfig, "[features]\nhooks = true\n");
        Seed(ProjectConfig, "model = \"o3\"\n\n[mcp_servers.other]\ncommand = \"o\"\n");

        Config().WriteForAgent(Folder, "proj", "api", "main");
        var project = File.ReadAllText(ProjectConfig);
        var user = File.ReadAllText(UserConfig);
        Config().WriteForAgent(Folder, "proj", "api", "main");

        Assert.Equal(project, File.ReadAllText(ProjectConfig));
        Assert.Equal(user, File.ReadAllText(UserConfig));
    }

    [Fact]
    public void Skills_go_to_the_agents_skill_directory()
    {
        Config().WriteForAgent(Folder, "proj", "api", "main");

        Assert.Equal(
            "---\nname: fl-tdd\n---\nbody\n",
            File.ReadAllText(Path.Combine(Folder, ".agents", "skills", "fl-tdd", "SKILL.md")));
    }

    [Fact]
    public void A_worktree_excludes_the_files_fleet_writes()
    {
        Config().WriteForAgent(Folder, "proj", "api", "main");

        var (folder, patterns) = Assert.Single(_excluded);
        Assert.Equal(Folder, folder);
        Assert.Equal(["/.codex/config.toml", "/.agents/", "/.fleet/", "/.fleet-ready"], patterns);
    }

    [Fact]
    public void An_agent_worktree_gets_no_instruction_file_of_fleet()
    {
        Config().WriteForAgent(Folder, "proj", "api", "main");

        Assert.False(File.Exists(Path.Combine(Folder, "AGENTS.md")));
    }
}
