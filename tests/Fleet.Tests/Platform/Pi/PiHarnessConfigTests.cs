using System.Text.Json.Nodes;
using Fleet.Platform.Pi;
using Fleet.Ports.Claude.Models;

namespace Fleet.Tests.Platform.Pi;

public sealed class PiHarnessConfigTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("fleet-pi-").FullName;

    private readonly List<(string Folder, IReadOnlyList<string> Patterns)> _excluded = [];

    public PiHarnessConfigTests() => Directory.CreateDirectory(Folder);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Folder => Path.Combine(_root, "wt");

    private string ProjectConfig => Path.Combine(Folder, ".pi", "mcp.json");

    private string TrustStore => Path.Combine(_root, "home", ".pi", "agent", "trust.json");

    private PiHarnessConfig Config() => new(
        (project, caller) => new McpServerEntry("fleet", "/bin/fleet", ["mcp", "--project", project, "--caller", caller]),
        new Dictionary<string, string> { ["fl-tdd"] = "---\nname: fl-tdd\n---\nbody\r\n" },
        (folder, patterns) => _excluded.Add((folder, patterns)),
        TrustStore);

    private JsonObject Written() => (JsonObject)JsonNode.Parse(File.ReadAllText(ProjectConfig))!;

    private static void Seed(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    [Fact]
    public void An_agent_gets_the_fleet_mcp_server_with_its_caller_declared_directly()
    {
        var written = Config().WriteForAgent(Folder, "proj", "api", "feat/x");

        Assert.True(written.Succeeded, written.Error);
        var fleet = Written()["mcpServers"]!["fleet"]!;
        Assert.Equal("/bin/fleet", fleet["command"]!.GetValue<string>());
        Assert.Equal(
            ["mcp", "--project", "proj", "--caller", "agent:api/feat/x"],
            fleet["args"]!.AsArray().Select(n => n!.GetValue<string>()));
        Assert.Equal("direct", fleet["exposure"]!.GetValue<string>());
    }

    [Fact]
    public void An_orchestration_folder_gets_the_server_with_the_given_caller_and_no_excludes()
    {
        var written = Config().WriteForOrchestration(Folder, "proj", "my-sub");

        Assert.True(written.Succeeded, written.Error);
        Assert.Equal(
            ["mcp", "--project", "proj", "--caller", "my-sub"],
            Written()["mcpServers"]!["fleet"]!["args"]!.AsArray().Select(n => n!.GetValue<string>()));
        Assert.Empty(_excluded);
    }

    [Fact]
    public void An_existing_config_keeps_its_other_settings_and_loses_the_old_fleet_server()
    {
        Seed(
            ProjectConfig,
            """{"autoEnableCodemode":false,"mcpServers":{"fleet":{"command":"old","env":{"X":"1"}},"other":{"url":"https://e"}}}""");

        Config().WriteForAgent(Folder, "proj", "api", "main");

        var root = Written();
        Assert.False(root["autoEnableCodemode"]!.GetValue<bool>());
        Assert.Equal("https://e", root["mcpServers"]!["other"]!["url"]!.GetValue<string>());
        Assert.Equal("/bin/fleet", root["mcpServers"]!["fleet"]!["command"]!.GetValue<string>());
        Assert.Null(root["mcpServers"]!["fleet"]!["env"]);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("[]")]
    [InlineData("""{"mcpServers":[]}""")]
    [InlineData("""{"mcpServers":{"a":1,"a":2}}""")]
    public void A_config_fleet_cannot_edit_safely_is_left_alone_and_reported(string text)
    {
        Seed(ProjectConfig, text);

        var written = Config().WriteForAgent(Folder, "proj", "api", "main");

        Assert.False(written.Succeeded);
        Assert.Contains(ProjectConfig, written.Error);
        Assert.Equal(text, File.ReadAllText(ProjectConfig));
        Assert.False(File.Exists(TrustStore));
    }

    [Fact]
    public void An_unreadable_config_is_reported_and_left_alone()
    {
        Directory.CreateDirectory(ProjectConfig);

        var written = Config().WriteForAgent(Folder, "proj", "api", "main");

        Assert.False(written.Succeeded);
        Assert.True(Directory.Exists(ProjectConfig));
    }

    [Fact]
    public void A_config_that_already_has_the_server_keeps_its_own_formatting()
    {
        var text = """
            { "mcpServers": { "fleet": { "exposure": "direct", "command": "/bin/fleet",
                "args": ["mcp", "--project", "proj", "--caller", "agent:api/main"] } } }
            """;
        Seed(ProjectConfig, text);

        var written = Config().WriteForAgent(Folder, "proj", "api", "main");

        Assert.True(written.Succeeded, written.Error);
        Assert.Equal(text, File.ReadAllText(ProjectConfig));
    }

    [Fact]
    public void Writing_twice_changes_nothing_the_second_time()
    {
        Seed(ProjectConfig, """{"mcpServers":{"other":{"command":"o"}}}""");
        Seed(TrustStore, "{\n  \"/elsewhere\": true\n}\n");

        Config().WriteForAgent(Folder, "proj", "api", "main");
        var project = File.ReadAllText(ProjectConfig);
        var trust = File.ReadAllText(TrustStore);
        var stamp = File.GetLastWriteTimeUtc(ProjectConfig);
        Config().WriteForAgent(Folder, "proj", "api", "main");

        Assert.Equal(project, File.ReadAllText(ProjectConfig));
        Assert.Equal(trust, File.ReadAllText(TrustStore));
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(ProjectConfig));
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
        Assert.Equal(["/.pi/mcp.json", "/.agents/", "/.fleet/", "/.fleet-ready"], patterns);
    }

    [Fact]
    public void An_agent_worktree_gets_no_instruction_file_of_fleet()
    {
        Config().WriteForAgent(Folder, "proj", "api", "main");

        Assert.False(File.Exists(Path.Combine(Folder, "AGENTS.md")));
    }

    [Fact]
    public void A_missing_trust_store_is_created_the_way_pi_writes_it()
    {
        var written = Config().WriteForAgent(Folder, "proj", "api", "main");

        Assert.True(written.Succeeded, written.Error);
        Assert.Equal("{\n  " + Quoted(Folder) + ": true\n}\n", File.ReadAllText(TrustStore));
    }

    [Fact]
    public void The_folder_is_trusted_next_to_the_decisions_already_saved_sorted_by_path()
    {
        Seed(TrustStore, """{"/zz": false, "/aa": true, "/gone": null}""");

        Config().WriteForAgent(Folder, "proj", "api", "main");

        var keys = ((JsonObject)JsonNode.Parse(File.ReadAllText(TrustStore))!).Select(p => p.Key).ToList();
        Assert.Equal(new[] { "/aa", "/gone", "/zz", Folder }.Order(StringComparer.Ordinal), keys);
        Assert.Contains("\"/zz\": false", File.ReadAllText(TrustStore));
        Assert.Contains("\"/gone\": null", File.ReadAllText(TrustStore));
        Assert.Contains(Quoted(Folder) + ": true", File.ReadAllText(TrustStore));
    }

    [Fact]
    public void An_already_trusted_folder_leaves_the_trust_store_byte_for_byte()
    {
        var text = "{ " + Quoted(Folder) + " :true }";
        Seed(TrustStore, text);

        var written = Config().WriteForAgent(Folder, "proj", "api", "main");

        Assert.True(written.Succeeded, written.Error);
        Assert.Equal(text, File.ReadAllText(TrustStore));
    }

    [Fact]
    public void A_trusted_parent_folder_covers_the_folder()
    {
        var text = "{ " + Quoted(_root) + ": true }";
        Seed(TrustStore, text);

        var written = Config().WriteForAgent(Folder, "proj", "api", "main");

        Assert.True(written.Succeeded, written.Error);
        Assert.Equal(text, File.ReadAllText(TrustStore));
    }

    [Fact]
    public void A_folder_the_user_declined_stays_declined_and_is_reported()
    {
        var text = "{ " + Quoted(Folder) + ": false }";
        Seed(TrustStore, text);

        var written = Config().WriteForAgent(Folder, "proj", "api", "main");

        Assert.False(written.Succeeded);
        Assert.Contains("not trust", written.Error);
        Assert.Contains(TrustStore, written.Error);
        Assert.Equal(text, File.ReadAllText(TrustStore));
        Assert.True(File.Exists(ProjectConfig));
    }

    [Fact]
    public void A_declined_parent_folder_also_keeps_the_folder_declined()
    {
        var text = "{ " + Quoted(_root) + ": false }";
        Seed(TrustStore, text);

        var written = Config().WriteForAgent(Folder, "proj", "api", "main");

        Assert.False(written.Succeeded);
        Assert.Equal(text, File.ReadAllText(TrustStore));
    }

    [Fact]
    public void The_closest_decision_wins_over_a_parent()
    {
        var text = "{ " + Quoted(_root) + ": false, " + Quoted(Folder) + ": true }";
        Seed(TrustStore, text);

        var written = Config().WriteForAgent(Folder, "proj", "api", "main");

        Assert.True(written.Succeeded, written.Error);
        Assert.Equal(text, File.ReadAllText(TrustStore));
    }

    [Fact]
    public void A_cleared_decision_for_the_folder_is_replaced_by_trust()
    {
        Seed(TrustStore, "{ " + Quoted(Folder) + ": null }");

        Config().WriteForAgent(Folder, "proj", "api", "main");

        Assert.Equal("{\n  " + Quoted(Folder) + ": true\n}\n", File.ReadAllText(TrustStore));
    }

    [Theory]
    [InlineData("{ broken")]
    [InlineData("[]")]
    [InlineData("""{"/a": "yes"}""")]
    [InlineData("""{"/a": 1}""")]
    public void A_trust_store_pi_would_reject_is_left_alone_and_reported(string text)
    {
        Seed(TrustStore, text);

        var written = Config().WriteForAgent(Folder, "proj", "api", "main");

        Assert.False(written.Succeeded);
        Assert.Contains(TrustStore, written.Error);
        Assert.Equal(text, File.ReadAllText(TrustStore));
        Assert.True(File.Exists(ProjectConfig));
        Assert.True(File.Exists(Path.Combine(Folder, ".agents", "skills", "fl-tdd", "SKILL.md")));
    }

    [Fact]
    public void A_folder_reached_through_a_link_is_trusted_under_its_real_path()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var link = Path.Combine(_root, "link");
        Directory.CreateSymbolicLink(link, _root);

        Config().WriteForAgent(Path.Combine(link, "wt"), "proj", "api", "main");

        Assert.Equal("{\n  " + Quoted(Folder) + ": true\n}\n", File.ReadAllText(TrustStore));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \n")]
    public void An_empty_trust_store_is_left_alone_because_pi_may_be_writing_it(string text)
    {
        Seed(TrustStore, text);

        var written = Config().WriteForAgent(Folder, "proj", "api", "main");

        Assert.False(written.Succeeded);
        Assert.Contains(TrustStore, written.Error);
        Assert.Equal(text, File.ReadAllText(TrustStore));
    }

    [Fact]
    public void A_trust_store_with_a_key_that_is_not_text_is_reported_and_left_alone()
    {
        var text = "{\"\\ud800\": true}";
        Seed(TrustStore, text);

        var written = Config().WriteForAgent(Folder, "proj", "api", "main");

        Assert.False(written.Succeeded);
        Assert.Equal(text, File.ReadAllText(TrustStore));
    }

    [Fact]
    public void A_saved_decision_with_a_trailing_separator_still_counts()
    {
        var text = "{ " + Quoted(Folder + Path.DirectorySeparatorChar) + ": true }";
        Seed(TrustStore, text);

        var written = Config().WriteForAgent(Folder, "proj", "api", "main");

        Assert.True(written.Succeeded, written.Error);
        Assert.Equal(text, File.ReadAllText(TrustStore));
    }

    [Fact]
    public void A_folder_behind_links_that_point_at_each_other_does_not_stop_fleet()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var a = Path.Combine(_root, "a");
        var b = Path.Combine(_root, "b");
        File.CreateSymbolicLink(a, Path.Combine(b, "x"));
        File.CreateSymbolicLink(b, a);

        Assert.Equal(Path.Combine(a, "y"), PiTrustStore.Canonical(Path.Combine(a, "y")));
    }

    [Fact]
    public void A_trust_store_that_is_a_link_stays_a_link_and_its_target_gets_the_decision()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var real = Path.Combine(_root, "dotfiles", "trust.json");
        Seed(real, "{}");
        Directory.CreateDirectory(Path.GetDirectoryName(TrustStore)!);
        File.CreateSymbolicLink(TrustStore, real);

        var written = Config().WriteForAgent(Folder, "proj", "api", "main");

        Assert.True(written.Succeeded, written.Error);
        Assert.NotNull(new FileInfo(TrustStore).LinkTarget);
        Assert.Equal("{\n  " + Quoted(Folder) + ": true\n}\n", File.ReadAllText(real));
    }

    [Fact]
    public void No_staging_file_is_left_next_to_the_files_fleet_writes()
    {
        Config().WriteForAgent(Folder, "proj", "api", "main");

        Assert.Equal(["mcp.json"], Directory.GetFiles(Path.Combine(Folder, ".pi")).Select(Path.GetFileName));
        Assert.Equal(["trust.json"], Directory.GetFiles(Path.GetDirectoryName(TrustStore)!).Select(Path.GetFileName));
    }

    [Fact]
    public void The_trust_store_follows_the_pi_agent_dir_and_falls_back_to_the_home_folder()
    {
        Assert.Equal(Path.Combine("/pa", "trust.json"), PiHarnessConfig.TrustStoreIn("/pa", "/h"));
        Assert.Equal(Path.Combine("/h", "x", "trust.json"), PiHarnessConfig.TrustStoreIn("~/x", "/h"));
        Assert.Equal(Path.Combine("/h", ".pi", "agent", "trust.json"), PiHarnessConfig.TrustStoreIn(" ", "/h"));
        Assert.Equal(Path.Combine("/h", ".pi", "agent", "trust.json"), PiHarnessConfig.TrustStoreIn(null, "/h"));
    }

    private static string Quoted(string path) =>
        "\"" + path.Replace("\\", "\\\\", StringComparison.Ordinal) + "\"";
}
