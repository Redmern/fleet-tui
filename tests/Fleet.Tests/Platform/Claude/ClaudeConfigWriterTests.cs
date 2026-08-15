using Fleet.Platform.Claude;
using Fleet.Ports.Claude.Models;

namespace Fleet.Tests.Platform.Claude;

public sealed class ClaudeConfigWriterTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "fleet-tests", Path.GetRandomFileName());

    public ClaudeConfigWriterTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private string McpPath => Path.Combine(_dir, ".mcp.json");

    private string SettingsPath => Path.Combine(_dir, ".claude", "settings.local.json");

    private ClaudePlan Plan(
        IReadOnlyList<string>? allow = null,
        IReadOnlyList<string>? deny = null,
        IReadOnlyList<string>? ask = null) =>
        new(
            _dir,
            new McpServerEntry("fleet", "fleet", ["mcp", "--project", "techweb"]),
            allow ?? ["mcp__fleet__list_agents"],
            deny ?? [],
            ask ?? [],
            ["fleet"]);

    [Fact]
    public void The_server_is_registered_as_a_stdio_command()
    {
        Assert.True(new ClaudeConfigWriter().Sync(Plan()).Succeeded);

        var mcp = File.ReadAllText(McpPath);

        Assert.Contains("\"fleet\"", mcp);
        Assert.Contains("\"type\": \"stdio\"", mcp);
        Assert.Contains("\"--project\"", mcp);
    }

    [Fact]
    public void The_server_is_enabled_so_the_first_run_prompt_is_suppressed()
    {
        new ClaudeConfigWriter().Sync(Plan());

        Assert.Contains("\"enabledMcpjsonServers\"", File.ReadAllText(SettingsPath));
        Assert.Contains("mcp__fleet__list_agents", File.ReadAllText(SettingsPath));
    }

    [Fact]
    public void Another_teams_mcp_server_and_unknown_keys_survive_the_merge()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(
            McpPath,
            """{"mcpServers":{"postgres":{"type":"stdio","command":"pg"}},"customKey":42}""");

        new ClaudeConfigWriter().Sync(Plan());

        var mcp = File.ReadAllText(McpPath);

        Assert.Contains("postgres", mcp);
        Assert.Contains("\"customKey\": 42", mcp);
        Assert.Contains("fleet", mcp);
    }

    [Fact]
    public void A_users_own_permissions_and_unknown_settings_survive_the_merge()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(
            SettingsPath,
            """{"permissions":{"allow":["Bash(ls)"]},"model":"opus"}""");

        new ClaudeConfigWriter().Sync(Plan());

        var settings = File.ReadAllText(SettingsPath);

        Assert.Contains("Bash(ls)", settings);
        Assert.Contains("\"model\": \"opus\"", settings);
        Assert.Contains("mcp__fleet__list_agents", settings);
    }

    [Fact]
    public void Re_syncing_does_not_pile_up_duplicate_fleet_rules()
    {
        var writer = new ClaudeConfigWriter();

        writer.Sync(Plan(allow: ["mcp__fleet__list_agents", "mcp__fleet__report"]));
        writer.Sync(Plan(allow: ["mcp__fleet__list_agents"]));

        var settings = File.ReadAllText(SettingsPath);

        Assert.Single(Occurrences(settings, "mcp__fleet__list_agents"));
        Assert.DoesNotContain("mcp__fleet__report", settings);
    }

    [Fact]
    public void The_fleet_server_is_only_enabled_once_even_after_repeated_syncs()
    {
        var writer = new ClaudeConfigWriter();

        writer.Sync(Plan());
        writer.Sync(Plan());

        Assert.Single(Occurrences(File.ReadAllText(SettingsPath), "\"fleet\""));
    }

    [Fact]
    public void Broken_json_is_left_untouched_rather_than_clobbered()
    {
        File.WriteAllText(McpPath, "{ not json at all");

        var result = new ClaudeConfigWriter().Sync(Plan());

        Assert.False(result.Succeeded);
        Assert.Equal("{ not json at all", File.ReadAllText(McpPath));
    }

    [Fact]
    public void Inspect_reports_the_server_registered_enabled_and_allowed()
    {
        new ClaudeConfigWriter().Sync(Plan());

        var state = new ClaudeConfigWriter().Inspect(_dir, "fleet");

        Assert.True(state.ServerRegistered);
        Assert.True(state.ServerEnabled);
        Assert.Contains("mcp__fleet__list_agents", state.Allow);
    }

    [Fact]
    public void Inspect_of_an_unconfigured_folder_reports_nothing()
    {
        var state = new ClaudeConfigWriter().Inspect(_dir, "fleet");

        Assert.False(state.ServerRegistered);
        Assert.False(state.ServerEnabled);
    }

    private static IEnumerable<int> Occurrences(string text, string token)
    {
        var index = text.IndexOf(token, StringComparison.Ordinal);

        while (index >= 0)
        {
            yield return index;
            index = text.IndexOf(token, index + token.Length, StringComparison.Ordinal);
        }
    }
}
