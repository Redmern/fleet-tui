using System.Text.Json.Nodes;
using Fleet.Platform.Opencode;
using Fleet.Ports.Claude.Models;

namespace Fleet.Tests.Platform.Opencode;

public sealed class OpencodeHarnessConfigTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("fleet-opencode-").FullName;

    private readonly List<(string Folder, IReadOnlyList<string> Patterns)> _excluded = [];

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private OpencodeHarnessConfig Config() => new(
        (project, caller) => new McpServerEntry("fleet", "/bin/fleet", ["mcp", "--project", project, "--caller", caller]),
        new Dictionary<string, string> { ["fl-tdd"] = "---\nname: fl-tdd\n---\nbody\r\n" },
        (folder, patterns) => _excluded.Add((folder, patterns)));

    private JsonObject Written() =>
        (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(_dir, "opencode.json")))!;

    [Fact]
    public void An_agent_gets_the_fleet_mcp_server_with_its_caller()
    {
        var written = Config().WriteForAgent(_dir, "proj", "api", "feat/x");

        Assert.True(written.Succeeded);
        var fleet = Written()["mcp"]!["fleet"]!;
        Assert.Equal("local", fleet["type"]!.GetValue<string>());
        Assert.Equal(
            ["/bin/fleet", "mcp", "--project", "proj", "--caller", "agent:api/feat/x"],
            fleet["command"]!.AsArray().Select(n => n!.GetValue<string>()));
    }

    [Fact]
    public void An_orchestration_folder_gets_the_server_with_the_given_caller()
    {
        Config().WriteForOrchestration(_dir, "proj", "my-sub");

        Assert.Equal(
            ["/bin/fleet", "mcp", "--project", "proj", "--caller", "my-sub"],
            Written()["mcp"]!["fleet"]!["command"]!.AsArray().Select(n => n!.GetValue<string>()));
        Assert.Empty(_excluded);
    }

    [Fact]
    public void An_existing_config_keeps_its_other_settings()
    {
        File.WriteAllText(
            Path.Combine(_dir, "opencode.json"),
            """{"model":"anthropic/x","mcp":{"other":{"type":"remote","url":"https://e"}}}""");

        Config().WriteForAgent(_dir, "proj", "api", "main");

        var root = Written();
        Assert.Equal("anthropic/x", root["model"]!.GetValue<string>());
        Assert.Equal("remote", root["mcp"]!["other"]!["type"]!.GetValue<string>());
        Assert.NotNull(root["mcp"]!["fleet"]);
    }

    [Fact]
    public void A_config_that_is_not_json_is_left_alone_and_reported()
    {
        File.WriteAllText(Path.Combine(_dir, "opencode.json"), "{ not json");

        var written = Config().WriteForAgent(_dir, "proj", "api", "main");

        Assert.False(written.Succeeded);
        Assert.Equal("{ not json", File.ReadAllText(Path.Combine(_dir, "opencode.json")));
    }

    [Fact]
    public void Skills_go_to_the_agents_skill_directory()
    {
        Config().WriteForAgent(_dir, "proj", "api", "main");

        Assert.Equal(
            "---\nname: fl-tdd\n---\nbody\n",
            File.ReadAllText(Path.Combine(_dir, ".agents", "skills", "fl-tdd", "SKILL.md")));
    }

    [Fact]
    public void A_worktree_excludes_the_files_fleet_writes()
    {
        Config().WriteForAgent(_dir, "proj", "api", "main");

        var (folder, patterns) = Assert.Single(_excluded);
        Assert.Equal(_dir, folder);
        Assert.Contains("/opencode.json", patterns);
        Assert.Contains("/.agents/", patterns);
        Assert.Contains("/.fleet/", patterns);
    }

    [Fact]
    public void An_agent_worktree_gets_no_instruction_file_of_fleet()
    {
        Config().WriteForAgent(_dir, "proj", "api", "main");

        Assert.False(File.Exists(Path.Combine(_dir, "AGENTS.md")));
    }
}
